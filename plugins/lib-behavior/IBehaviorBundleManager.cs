namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Interface for managing behavior bundles for efficient storage and retrieval.
/// Behaviors can be grouped into bundles for bulk download by clients.
/// </summary>
public interface IBehaviorBundleManager
{
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
    Task<bool> RecordBehaviorAsync(
        string behaviorId,
        string assetId,
        string? bundleId,
        BehaviorMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a behavior to a bundle. Creates the bundle if it doesn't exist.
    /// </summary>
    /// <param name="behaviorId">The behavior's unique identifier.</param>
    /// <param name="assetId">The asset service ID for the behavior.</param>
    /// <param name="bundleId">The bundle identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddToBundleAsync(
        string behaviorId,
        string assetId,
        string bundleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an asset bundle from all behaviors in a bundle group.
    /// Call this after adding multiple behaviors to efficiently package them.
    /// </summary>
    /// <param name="bundleId">The bundle identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The asset bundle ID, or null if no behaviors in bundle.</returns>
    Task<string?> CreateAssetBundleAsync(
        string bundleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the metadata for a behavior.
    /// </summary>
    /// <param name="behaviorId">The behavior's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The behavior metadata, or null if not found.</returns>
    Task<BehaviorMetadata?> GetMetadataAsync(
        string behaviorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the bundle membership for a bundle.
    /// </summary>
    /// <param name="bundleId">The bundle identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bundle membership, or null if not found.</returns>
    Task<BundleMembership?> GetBundleMembershipAsync(
        string bundleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a behavior from storage and any bundles.
    /// </summary>
    /// <param name="behaviorId">The behavior's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The metadata of the deleted behavior, or null if not found.</returns>
    Task<BehaviorMetadata?> RemoveBehaviorAsync(
        string behaviorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves GOAP metadata for a compiled behavior.
    /// </summary>
    /// <param name="behaviorId">The behavior's unique identifier.</param>
    /// <param name="metadata">The GOAP metadata to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveGoapMetadataAsync(
        string behaviorId,
        CachedGoapMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets GOAP metadata for a behavior.
    /// </summary>
    /// <param name="behaviorId">The behavior's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The GOAP metadata, or null if not found.</returns>
    Task<CachedGoapMetadata?> GetGoapMetadataAsync(
        string behaviorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes GOAP metadata for a behavior.
    /// </summary>
    /// <param name="behaviorId">The behavior's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if metadata was deleted, false if not found.</returns>
    Task<bool> RemoveGoapMetadataAsync(
        string behaviorId,
        CancellationToken cancellationToken = default);
}
