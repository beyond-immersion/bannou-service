using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Achievement.Sync;

/// <summary>
/// Xbox achievement sync implementation (stub).
/// </summary>
/// <remarks>
/// Xbox achievements require Xbox Live integration through the
/// Microsoft Game Development Kit (GDK). Implementation would
/// require XBL title ID, service config ID, and proper authentication.
/// </remarks>
public class XboxAchievementSync : IPlatformAchievementSync
{
    private readonly ILogger<XboxAchievementSync> _logger;

    /// <inheritdoc />
    public Platform Platform => Platform.Xbox;

    /// <inheritdoc />
    /// <remarks>Xbox sync is a stub and is never configured.</remarks>
    public bool IsConfigured => false;

    /// <summary>
    /// Initializes a new instance of the XboxAchievementSync.
    /// </summary>
    public XboxAchievementSync(ILogger<XboxAchievementSync> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> IsLinkedAsync(Guid accountId, CancellationToken ct = default)
    {
        _logger.LogDebug("Xbox sync not implemented - IsLinkedAsync returning false for {AccountId}", accountId);
        await Task.CompletedTask;
        return false;
    }

    /// <inheritdoc />
    public async Task<string?> GetExternalIdAsync(Guid accountId, CancellationToken ct = default)
    {
        _logger.LogDebug("Xbox sync not implemented - GetExternalIdAsync returning null for {AccountId}", accountId);
        await Task.CompletedTask;
        return null;
    }

    /// <inheritdoc />
    public async Task<PlatformSyncResult> UnlockAsync(string externalUserId, string platformAchievementId, CancellationToken ct = default)
    {
        _logger.LogWarning("Xbox achievement sync not implemented - {AchievementId} for {UserId}",
            platformAchievementId, externalUserId);

        await Task.CompletedTask;
        return new PlatformSyncResult
        {
            Success = false,
            ErrorMessage = "Xbox achievement sync not implemented"
        };
    }

    /// <inheritdoc />
    public async Task<PlatformSyncResult> SetProgressAsync(string externalUserId, string platformAchievementId, int current, int target, CancellationToken ct = default)
    {
        _logger.LogWarning("Xbox progress sync not implemented - {AchievementId} ({Current}/{Target}) for {UserId}",
            platformAchievementId, current, target, externalUserId);

        await Task.CompletedTask;
        return new PlatformSyncResult
        {
            Success = false,
            ErrorMessage = "Xbox progress sync not implemented"
        };
    }
}
