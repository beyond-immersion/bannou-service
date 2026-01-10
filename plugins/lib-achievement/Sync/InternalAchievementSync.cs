using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Achievement.Sync;

/// <summary>
/// Internal (Bannou-only) achievement sync implementation.
/// </summary>
/// <remarks>
/// This implementation is a no-op since internal achievements
/// don't need to sync anywhere - they exist only within Bannou.
/// All operations succeed immediately without any external calls.
/// </remarks>
public class InternalAchievementSync : IPlatformAchievementSync
{
    private readonly ILogger<InternalAchievementSync> _logger;

    /// <inheritdoc />
    public Platform Platform => Platform.Internal;

    /// <summary>
    /// Initializes a new instance of the InternalAchievementSync.
    /// </summary>
    public InternalAchievementSync(ILogger<InternalAchievementSync> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> IsLinkedAsync(Guid accountId, CancellationToken ct = default)
    {
        // Internal achievements are always "linked" - no external account needed
        _logger.LogDebug("Internal platform is always linked for account {AccountId}", accountId);
        await Task.CompletedTask;
        return true;
    }

    /// <inheritdoc />
    public async Task<string?> GetExternalIdAsync(Guid accountId, CancellationToken ct = default)
    {
        // For internal achievements, the external ID is the same as the account ID
        _logger.LogDebug("Internal platform using account ID as external ID for {AccountId}", accountId);
        await Task.CompletedTask;
        return accountId.ToString();
    }

    /// <inheritdoc />
    public async Task<PlatformSyncResult> UnlockAsync(string externalUserId, string platformAchievementId, CancellationToken ct = default)
    {
        // No-op: internal achievements don't need external sync
        _logger.LogDebug("Internal unlock (no-op) for {AchievementId} for {UserId}",
            platformAchievementId, externalUserId);

        await Task.CompletedTask;
        return new PlatformSyncResult
        {
            Success = true,
            SyncId = Guid.NewGuid().ToString()
        };
    }

    /// <inheritdoc />
    public async Task<PlatformSyncResult> SetProgressAsync(string externalUserId, string platformAchievementId, int current, int target, CancellationToken ct = default)
    {
        // No-op: internal achievements don't need external sync
        _logger.LogDebug("Internal progress (no-op) for {AchievementId} ({Current}/{Target}) for {UserId}",
            platformAchievementId, current, target, externalUserId);

        await Task.CompletedTask;
        return new PlatformSyncResult
        {
            Success = true,
            SyncId = Guid.NewGuid().ToString()
        };
    }
}
