using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Achievement.Sync;

/// <summary>
/// PlayStation trophy sync implementation (stub).
/// </summary>
/// <remarks>
/// PlayStation trophies require PSN integration through the
/// PlayStation Partners program. Implementation would require
/// NP Title ID, service config, and proper PlayStation Network authentication.
/// Note: PlayStation calls achievements "trophies".
/// </remarks>
public class PlayStationAchievementSync : IPlatformAchievementSync
{
    private readonly ILogger<PlayStationAchievementSync> _logger;

    /// <inheritdoc />
    public Platform Platform => Platform.Playstation;

    /// <summary>
    /// Initializes a new instance of the PlayStationAchievementSync.
    /// </summary>
    public PlayStationAchievementSync(ILogger<PlayStationAchievementSync> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<bool> IsLinkedAsync(Guid accountId, CancellationToken ct = default)
    {
        _logger.LogDebug("PlayStation sync not implemented - IsLinkedAsync returning false for {AccountId}", accountId);
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<string?> GetExternalIdAsync(Guid accountId, CancellationToken ct = default)
    {
        _logger.LogDebug("PlayStation sync not implemented - GetExternalIdAsync returning null for {AccountId}", accountId);
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<PlatformSyncResult> UnlockAsync(string externalUserId, string platformAchievementId, CancellationToken ct = default)
    {
        _logger.LogWarning("PlayStation trophy sync not implemented - {TrophyId} for {UserId}",
            platformAchievementId, externalUserId);

        return Task.FromResult(new PlatformSyncResult
        {
            Success = false,
            ErrorMessage = "PlayStation trophy sync not implemented"
        });
    }

    /// <inheritdoc />
    public Task<PlatformSyncResult> SetProgressAsync(string externalUserId, string platformAchievementId, int current, int target, CancellationToken ct = default)
    {
        _logger.LogWarning("PlayStation trophy progress sync not implemented - {TrophyId} ({Current}/{Target}) for {UserId}",
            platformAchievementId, current, target, externalUserId);

        return Task.FromResult(new PlatformSyncResult
        {
            Success = false,
            ErrorMessage = "PlayStation trophy progress sync not implemented"
        });
    }
}
