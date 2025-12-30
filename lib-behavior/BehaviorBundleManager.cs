using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Manages behavior bundles for efficient storage and retrieval.
/// Behaviors can be grouped into bundles for bulk download by clients.
/// </summary>
public class BehaviorBundleManager
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IAssetClient _assetClient;
    private readonly ILogger<BehaviorBundleManager> _logger;

    private const string STATE_STORE = "behavior-statestore";
    private const string BUNDLE_MEMBERSHIP_PREFIX = "bundle-membership:";
    private const string BEHAVIOR_METADATA_PREFIX = "behavior-metadata:";
    private const string GOAP_METADATA_PREFIX = "goap-metadata:";

    /// <summary>
    /// Initializes a new instance of the <see cref="BehaviorBundleManager"/> class.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for persistence.</param>
    /// <param name="assetClient">Asset client for bundle operations.</param>
    /// <param name="logger">Logger for structured logging.</param>
    public BehaviorBundleManager(
        IStateStoreFactory stateStoreFactory,
        IAssetClient assetClient,
        ILogger<BehaviorBundleManager> logger)
    {
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _assetClient = assetClient ?? throw new ArgumentNullException(nameof(assetClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        ArgumentNullException.ThrowIfNull(metadata, nameof(metadata));

        _logger.LogDebug(
            "Recording behavior {BehaviorId} with asset {AssetId}, bundle {BundleId}",
            behaviorId,
            assetId,
            bundleId ?? "none");

        var metadataStore = _stateStoreFactory.GetStore<BehaviorMetadata>(STATE_STORE);
        var metadataKey = $"{BEHAVIOR_METADATA_PREFIX}{behaviorId}";

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

        var membershipStore = _stateStoreFactory.GetStore<BundleMembership>(STATE_STORE);
        var membershipKey = $"{BUNDLE_MEMBERSHIP_PREFIX}{bundleId}";

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

        var membershipStore = _stateStoreFactory.GetStore<BundleMembership>(STATE_STORE);
        var membershipKey = $"{BUNDLE_MEMBERSHIP_PREFIX}{bundleId}";

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
                    Bundle_id = $"behavior-bundle-{bundleId}",
                    Version = "1.0.0",
                    Asset_ids = assetIds,
                    Compression = CompressionType.Lz4
                },
                cancellationToken);

            membership.AssetBundleId = response.Bundle_id;
            await membershipStore.SaveAsync(membershipKey, membership, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Created asset bundle {AssetBundleId} for behavior bundle {BundleId} with {Count} behaviors",
                response.Bundle_id,
                bundleId,
                assetIds.Count);

            return response.Bundle_id;
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

        var metadataStore = _stateStoreFactory.GetStore<BehaviorMetadata>(STATE_STORE);
        var metadataKey = $"{BEHAVIOR_METADATA_PREFIX}{behaviorId}";

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

        var membershipStore = _stateStoreFactory.GetStore<BundleMembership>(STATE_STORE);
        var membershipKey = $"{BUNDLE_MEMBERSHIP_PREFIX}{bundleId}";

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

        var metadataStore = _stateStoreFactory.GetStore<BehaviorMetadata>(STATE_STORE);
        var metadataKey = $"{BEHAVIOR_METADATA_PREFIX}{behaviorId}";

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
            var membershipStore = _stateStoreFactory.GetStore<BundleMembership>(STATE_STORE);
            var membershipKey = $"{BUNDLE_MEMBERSHIP_PREFIX}{metadata.BundleId}";

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
        ArgumentNullException.ThrowIfNull(metadata, nameof(metadata));

        var store = _stateStoreFactory.GetStore<CachedGoapMetadata>(STATE_STORE);
        var key = $"{GOAP_METADATA_PREFIX}{behaviorId}";

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
    public async Task<CachedGoapMetadata?> GetGoapMetadataAsync(
        string behaviorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId, nameof(behaviorId));

        var store = _stateStoreFactory.GetStore<CachedGoapMetadata>(STATE_STORE);
        var key = $"{GOAP_METADATA_PREFIX}{behaviorId}";

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

        var store = _stateStoreFactory.GetStore<CachedGoapMetadata>(STATE_STORE);
        var key = $"{GOAP_METADATA_PREFIX}{behaviorId}";

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

/// <summary>
/// Metadata for a compiled behavior stored in the system.
/// </summary>
public class BehaviorMetadata
{
    /// <summary>
    /// Unique identifier for the behavior (content-addressable hash).
    /// </summary>
    [JsonPropertyName("behaviorId")]
    public string BehaviorId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name of the behavior.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Category of the behavior (e.g., professional, cultural).
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>
    /// Asset service ID where bytecode is stored.
    /// </summary>
    [JsonPropertyName("assetId")]
    public string AssetId { get; set; } = string.Empty;

    /// <summary>
    /// Bundle ID this behavior belongs to, if any.
    /// </summary>
    [JsonPropertyName("bundleId")]
    public string? BundleId { get; set; }

    /// <summary>
    /// Size of the compiled bytecode in bytes.
    /// </summary>
    [JsonPropertyName("bytecodeSize")]
    public int BytecodeSize { get; set; }

    /// <summary>
    /// ABML schema version used for compilation.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    /// <summary>
    /// When the behavior was first compiled.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the behavior was last updated.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Tracks which behaviors belong to a bundle.
/// </summary>
public class BundleMembership
{
    /// <summary>
    /// The bundle identifier.
    /// </summary>
    [JsonPropertyName("bundleId")]
    public string BundleId { get; set; } = string.Empty;

    /// <summary>
    /// Map of behavior ID to asset ID for all behaviors in this bundle.
    /// </summary>
    [JsonPropertyName("behaviorAssetIds")]
    public Dictionary<string, string> BehaviorAssetIds { get; set; } = new();

    /// <summary>
    /// The asset bundle ID if one has been created.
    /// </summary>
    [JsonPropertyName("assetBundleId")]
    public string? AssetBundleId { get; set; }

    /// <summary>
    /// When the bundle was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the bundle was last updated.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Cached GOAP metadata for a compiled behavior.
/// Stores goals and actions as serializable string dictionaries.
/// </summary>
public class CachedGoapMetadata
{
    /// <summary>
    /// The behavior ID this metadata belongs to.
    /// </summary>
    [JsonPropertyName("behaviorId")]
    public string BehaviorId { get; set; } = string.Empty;

    /// <summary>
    /// GOAP goals defined in the behavior's goals: section.
    /// </summary>
    [JsonPropertyName("goals")]
    public List<CachedGoapGoal> Goals { get; set; } = new();

    /// <summary>
    /// GOAP actions extracted from flows with goap: blocks.
    /// </summary>
    [JsonPropertyName("actions")]
    public List<CachedGoapAction> Actions { get; set; } = new();

    /// <summary>
    /// When this metadata was cached.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Cached GOAP goal from a behavior's goals: section.
/// </summary>
public class CachedGoapGoal
{
    /// <summary>
    /// Goal name (key in the goals: section).
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Goal priority (higher = more important).
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    /// <summary>
    /// Goal conditions as string key-value pairs (e.g., "hunger" -> "<= 0.3").
    /// </summary>
    [JsonPropertyName("conditions")]
    public Dictionary<string, string> Conditions { get; set; } = new();
}

/// <summary>
/// Cached GOAP action from a flow's goap: block.
/// </summary>
public class CachedGoapAction
{
    /// <summary>
    /// Flow name (becomes the action ID).
    /// </summary>
    [JsonPropertyName("flowName")]
    public string FlowName { get; set; } = string.Empty;

    /// <summary>
    /// Action preconditions as string key-value pairs (e.g., "gold" -> ">= 5").
    /// </summary>
    [JsonPropertyName("preconditions")]
    public Dictionary<string, string> Preconditions { get; set; } = new();

    /// <summary>
    /// Action effects as string key-value pairs (e.g., "hunger" -> "-0.8").
    /// </summary>
    [JsonPropertyName("effects")]
    public Dictionary<string, string> Effects { get; set; } = new();

    /// <summary>
    /// Cost of performing this action.
    /// </summary>
    [JsonPropertyName("cost")]
    public float Cost { get; set; } = 1.0f;
}
