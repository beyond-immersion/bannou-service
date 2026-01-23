using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.SaveLoad.Models;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.SaveLoad.Processing;

/// <summary>
/// Background service that runs scheduled cleanup of expired save versions.
/// Only runs on the control plane instance when CleanupControlPlaneOnly is true.
/// </summary>
public class CleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SaveLoadServiceConfiguration _configuration;
    private readonly AppConfiguration _appConfiguration;
    private readonly ILogger<CleanupService> _logger;

    /// <summary>
    /// Creates a new CleanupService instance.
    /// </summary>
    public CleanupService(
        IServiceProvider serviceProvider,
        SaveLoadServiceConfiguration configuration,
        AppConfiguration appConfiguration,
        ILogger<CleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _appConfiguration = appConfiguration;
        _logger = logger;
    }

    /// <summary>
    /// Executes the cleanup background processing loop.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Check if cleanup should only run on control plane
        if (_configuration.CleanupControlPlaneOnly)
        {
            // Check if we are the control plane instance by comparing effective app ID to default
            var effectiveAppId = _appConfiguration.EffectiveAppId;
            var defaultAppId = AppConstants.DEFAULT_APP_NAME;

            if (effectiveAppId != defaultAppId)
            {
                _logger.LogInformation(
                    "CleanupService not starting: not control plane (EffectiveAppId={Effective}, DefaultAppId={Default})",
                    effectiveAppId, defaultAppId);
                return;
            }
        }

        // Wait for other services to initialize
        await Task.Delay(TimeSpan.FromSeconds(_configuration.CleanupStartupDelaySeconds), stoppingToken);

        _logger.LogInformation(
            "CleanupService starting with interval of {IntervalMinutes} minutes",
            _configuration.CleanupIntervalMinutes);

        var interval = TimeSpan.FromMinutes(_configuration.CleanupIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                await RunScheduledCleanupAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled cleanup");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("CleanupService stopped");
    }

    private async Task RunScheduledCleanupAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Running scheduled cleanup");

        var stateStoreFactory = serviceProvider.GetRequiredService<IStateStoreFactory>();
        var messageBus = serviceProvider.GetRequiredService<IMessageBus>();
        var assetClient = serviceProvider.GetRequiredService<IAssetClient>();

        var slotStore = stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        var versionStore = stateStoreFactory.GetQueryableStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);

        // Get all slots
        var slots = await slotStore.QueryAsync(_ => true, cancellationToken);
        var slotList = slots.ToList();

        var totalVersionsDeleted = 0;
        var totalSlotsDeleted = 0;
        long totalBytesFreed = 0;

        var sessionGracePeriod = TimeSpan.FromMinutes(_configuration.SessionCleanupGracePeriodMinutes);

        foreach (var slot in slotList)
        {
            // Skip SESSION-owned slots that are within the grace period
            if (string.Equals(slot.OwnerType, "SESSION", StringComparison.OrdinalIgnoreCase) &&
                DateTimeOffset.UtcNow - slot.UpdatedAt < sessionGracePeriod)
            {
                continue;
            }

            var (versionsDeleted, bytesFreed) = await CleanupSlotAsync(
                slot, versionStore, assetClient, cancellationToken);

            totalVersionsDeleted += versionsDeleted;
            totalBytesFreed += bytesFreed;

            // Check if slot should be deleted (all versions removed or expired)
            if (slot.VersionCount == 0 && versionsDeleted > 0)
            {
                await slotStore.DeleteAsync(slot.GetStateKey(), cancellationToken);
                totalSlotsDeleted++;
            }
        }

        if (totalVersionsDeleted > 0 || totalSlotsDeleted > 0)
        {
            _logger.LogInformation(
                "Scheduled cleanup completed: {Versions} versions deleted, {Slots} slots deleted, {Bytes} bytes freed",
                totalVersionsDeleted, totalSlotsDeleted, totalBytesFreed);

            await messageBus.TryPublishAsync(
                "save-load.cleanup.completed",
                new CleanupCompletedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    VersionsDeleted = totalVersionsDeleted,
                    SlotsDeleted = totalSlotsDeleted,
                    BytesFreed = totalBytesFreed
                },
                cancellationToken: cancellationToken);
        }
        else
        {
            _logger.LogDebug("Scheduled cleanup: no items to clean");
        }
    }

    private async Task<(int versionsDeleted, long bytesFreed)> CleanupSlotAsync(
        SaveSlotMetadata slot,
        IQueryableStateStore<SaveVersionManifest> versionStore,
        IAssetClient assetClient,
        CancellationToken cancellationToken)
    {
        // Get versions for this slot
        var versions = await versionStore.QueryAsync(v => v.SlotId == slot.SlotId, cancellationToken);
        var versionList = versions.OrderByDescending(v => v.VersionNumber).ToList();

        if (versionList.Count == 0)
        {
            return (0, 0);
        }

        var versionsDeleted = 0;
        long bytesFreed = 0;

        // Apply rolling cleanup based on max versions (but skip pinned)
        var unpinnedVersions = versionList.Where(v => !v.IsPinned).ToList();
        var pinnedVersions = versionList.Where(v => v.IsPinned).ToList();

        // Keep max versions (minus pinned count) of unpinned versions
        var effectiveMaxVersions = Math.Max(1, slot.MaxVersions - pinnedVersions.Count);
        var versionsToDelete = unpinnedVersions.Skip(effectiveMaxVersions).ToList();

        // Also delete versions past retention days (if configured)
        if (slot.RetentionDays.HasValue && slot.RetentionDays.Value > 0)
        {
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-slot.RetentionDays.Value);
            var expiredVersions = unpinnedVersions
                .Where(v => v.CreatedAt < cutoffDate && !versionsToDelete.Contains(v))
                .ToList();
            versionsToDelete.AddRange(expiredVersions);
        }

        foreach (var version in versionsToDelete)
        {
            try
            {
                // Delete the version from state store
                var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, version.VersionNumber);
                await versionStore.DeleteAsync(versionKey, cancellationToken);

                bytesFreed += version.SizeBytes;
                versionsDeleted++;

                // Delete asset if exists
                if (!string.IsNullOrEmpty(version.AssetId))
                {
                    try
                    {
                        await assetClient.DeleteAssetAsync(
                            new DeleteAssetRequest { AssetId = version.AssetId },
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete asset {AssetId}", version.AssetId);
                    }
                }

                // Delete thumbnail if exists
                if (!string.IsNullOrEmpty(version.ThumbnailAssetId))
                {
                    try
                    {
                        await assetClient.DeleteAssetAsync(
                            new DeleteAssetRequest { AssetId = version.ThumbnailAssetId },
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete thumbnail asset {AssetId}", version.ThumbnailAssetId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete version {Version} for slot {SlotId}",
                    version.VersionNumber, slot.SlotId);
            }
        }

        // Log delta chains that need collapsing (only when auto-collapse is enabled)
        if (_configuration.AutoCollapseEnabled && versionsDeleted > 0)
        {
            var remainingVersions = versionList.Except(versionsToDelete).ToList();
            var deltaVersions = remainingVersions.Where(v => v.IsDelta).ToList();
            if (deltaVersions.Count > 0)
            {
                _logger.LogInformation(
                    "Slot {SlotId} has {DeltaCount} delta versions that could be collapsed",
                    slot.SlotId, deltaVersions.Count);
            }
        }

        // Update slot metadata if versions were deleted
        if (versionsDeleted > 0)
        {
            slot.VersionCount -= versionsDeleted;
            slot.TotalSizeBytes -= bytesFreed;
            slot.UpdatedAt = DateTimeOffset.UtcNow;

            using var scope = _serviceProvider.CreateScope();
            var slotStore = scope.ServiceProvider
                .GetRequiredService<IStateStoreFactory>()
                .GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            await slotStore.SaveAsync(slot.GetStateKey(), slot, cancellationToken: cancellationToken);
        }

        return (versionsDeleted, bytesFreed);
    }
}
