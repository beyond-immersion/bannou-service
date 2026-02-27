using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

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
    private readonly ITelemetryProvider _telemetryProvider;

    /// <inheritdoc />
    public Platform Platform => Platform.Playstation;

    /// <inheritdoc />
    /// <remarks>PlayStation sync is a stub and is never configured.</remarks>
    public bool IsConfigured => false;

    /// <summary>
    /// Initializes a new instance of the PlayStationAchievementSync.
    /// </summary>
    public PlayStationAchievementSync(ILogger<PlayStationAchievementSync> logger, ITelemetryProvider telemetryProvider)
    {
        _logger = logger;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc />
    public async Task<bool> IsLinkedAsync(Guid accountId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "PlayStationAchievementSync.IsLinkedAsync");
        _logger.LogDebug("PlayStation sync not implemented - IsLinkedAsync returning false for {AccountId}", accountId);
        await Task.CompletedTask;
        return false;
    }

    /// <inheritdoc />
    public async Task<string?> GetExternalIdAsync(Guid accountId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "PlayStationAchievementSync.GetExternalIdAsync");
        _logger.LogDebug("PlayStation sync not implemented - GetExternalIdAsync returning null for {AccountId}", accountId);
        await Task.CompletedTask;
        return null;
    }

    /// <inheritdoc />
    public async Task<PlatformSyncResult> UnlockAsync(string externalUserId, string platformAchievementId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "PlayStationAchievementSync.UnlockAsync");
        _logger.LogWarning("PlayStation trophy sync not implemented - {TrophyId} for {UserId}",
            platformAchievementId, externalUserId);

        await Task.CompletedTask;
        return new PlatformSyncResult
        {
            Success = false,
            ErrorMessage = "PlayStation trophy sync not implemented"
        };
    }

    /// <inheritdoc />
    public async Task<PlatformSyncResult> SetProgressAsync(string externalUserId, string platformAchievementId, int current, int target, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "PlayStationAchievementSync.SetProgressAsync");
        _logger.LogWarning("PlayStation trophy progress sync not implemented - {TrophyId} ({Current}/{Target}) for {UserId}",
            platformAchievementId, current, target, externalUserId);

        await Task.CompletedTask;
        return new PlatformSyncResult
        {
            Success = false,
            ErrorMessage = "PlayStation trophy progress sync not implemented"
        };
    }
}
