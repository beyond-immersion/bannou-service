using BeyondImmersion.BannouService.SaveLoad.Models;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.SaveLoad.Helpers;

/// <summary>
/// Handles version cleanup operations including rolling cleanup
/// and slot-level version management.
/// </summary>
/// <remarks>
/// Extracted from SaveLoadService for improved testability
/// and separation of cleanup concerns.
/// </remarks>
public interface IVersionCleanupManager
{
    /// <summary>
    /// Performs rolling cleanup of old versions based on slot configuration.
    /// Deletes oldest non-pinned versions when slot exceeds max versions.
    /// </summary>
    /// <param name="slot">The slot metadata with version limits.</param>
    /// <param name="versionStore">The version manifest store.</param>
    /// <param name="hotCacheStore">The hot cache store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of versions cleaned up.</returns>
    Task<int> PerformRollingCleanupAsync(
        SaveSlotMetadata slot,
        IStateStore<SaveVersionManifest> versionStore,
        IStateStore<HotSaveEntry> hotCacheStore,
        CancellationToken cancellationToken);

    /// <summary>
    /// Cleans up old versions for a slot, including asset deletion
    /// and event publishing. Queries stores internally.
    /// </summary>
    /// <param name="slot">The slot metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CleanupOldVersionsAsync(
        SaveSlotMetadata slot,
        CancellationToken cancellationToken);
}
