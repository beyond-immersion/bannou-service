namespace BeyondImmersion.BannouService.Achievement.Sync;

/// <summary>
/// Result of a platform sync operation.
/// </summary>
public class PlatformSyncResult
{
    /// <summary>
    /// Whether the sync was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the sync failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Platform-specific sync identifier (for tracking).
    /// </summary>
    public string? SyncId { get; set; }
}

/// <summary>
/// Interface for syncing achievements to external platforms (Steam, Xbox, PlayStation).
/// </summary>
/// <remarks>
/// Each platform implementation handles the specifics of communicating with
/// that platform's achievement API. Implementations should handle rate limiting
/// and retry logic internally.
/// </remarks>
public interface IPlatformAchievementSync
{
    /// <summary>
    /// Gets the platform this sync handles.
    /// </summary>
    Platform Platform { get; }

    /// <summary>
    /// Checks if an account is linked to this platform.
    /// </summary>
    /// <param name="accountId">The Bannou account ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the account is linked to this platform.</returns>
    Task<bool> IsLinkedAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Gets the external platform user ID for an account.
    /// </summary>
    /// <param name="accountId">The Bannou account ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The platform-specific user ID, or null if not linked.</returns>
    Task<string?> GetExternalIdAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Unlocks an achievement on the platform.
    /// </summary>
    /// <param name="externalUserId">Platform-specific user ID.</param>
    /// <param name="platformAchievementId">Platform-specific achievement ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the unlock operation.</returns>
    Task<PlatformSyncResult> UnlockAsync(string externalUserId, string platformAchievementId, CancellationToken ct = default);

    /// <summary>
    /// Sets progress on a progressive achievement.
    /// </summary>
    /// <param name="externalUserId">Platform-specific user ID.</param>
    /// <param name="platformAchievementId">Platform-specific achievement ID.</param>
    /// <param name="current">Current progress value.</param>
    /// <param name="target">Target progress value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the progress update operation.</returns>
    Task<PlatformSyncResult> SetProgressAsync(string externalUserId, string platformAchievementId, int current, int target, CancellationToken ct = default);
}
