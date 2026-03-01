using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Asset.Bundles;

/// <summary>
/// Background service that periodically scans the ZIP cache storage path
/// and removes expired ZIP conversions older than the configured TTL.
/// </summary>
public sealed class ZipCacheCleanupWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<ZipCacheCleanupWorker> _logger;
    private readonly AssetServiceConfiguration _configuration;

    /// <summary>
    /// Creates a new ZipCacheCleanupWorker.
    /// </summary>
    public ZipCacheCleanupWorker(
        IServiceProvider serviceProvider,
        ITelemetryProvider telemetryProvider,
        ILogger<ZipCacheCleanupWorker> logger,
        AssetServiceConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_configuration.ZipCacheCleanupIntervalMinutes);
        _logger.LogInformation("ZIP cache cleanup worker starting, interval: {Interval}, TTL: {TtlHours} hours",
            interval, _configuration.ZipCacheTtlHours);

        // Startup delay to allow other services to initialize
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredZipCacheAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during ZIP cache cleanup scan");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "asset",
                        "ZipCacheCleanup",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    _logger.LogDebug(pubEx, "Failed to publish error event - continuing cleanup loop");
                }
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("ZIP cache cleanup worker stopped");
    }

    /// <summary>
    /// Scans the ZIP cache storage prefix and deletes objects older than the configured TTL.
    /// </summary>
    internal async Task CleanupExpiredZipCacheAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.asset", "ZipCacheCleanupWorker.CleanupExpiredZipCacheAsync");

        var storageProvider = _serviceProvider.GetService<IAssetStorageProvider>();
        if (storageProvider == null)
        {
            _logger.LogDebug("ZipCacheCleanup: Storage provider not available, skipping");
            return;
        }

        var bucket = _configuration.StorageBucket;
        var prefix = _configuration.BundleZipCachePathPrefix;
        var ttlHours = _configuration.ZipCacheTtlHours > 0 ? _configuration.ZipCacheTtlHours : 24;
        var cutoff = DateTime.UtcNow.AddHours(-ttlHours);

        var objects = await storageProvider.ListObjectsByPrefixAsync(bucket, prefix);
        if (objects.Count == 0)
        {
            _logger.LogDebug("ZipCacheCleanup: No objects in ZIP cache prefix");
            return;
        }

        _logger.LogInformation("ZipCacheCleanup: Scanning {Count} ZIP cache objects, cutoff: {Cutoff}",
            objects.Count, cutoff);

        var deletedCount = 0;

        foreach (var obj in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (obj.LastModified < cutoff)
            {
                try
                {
                    await storageProvider.DeleteObjectAsync(bucket, obj.Key);
                    deletedCount++;
                    _logger.LogDebug("ZipCacheCleanup: Deleted expired cache entry {Key} (modified: {Modified})",
                        obj.Key, obj.LastModified);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ZipCacheCleanup: Failed to delete cache entry {Key}", obj.Key);
                }
            }
        }

        if (deletedCount > 0)
        {
            _logger.LogInformation("ZipCacheCleanup: Deleted {Count} expired ZIP cache entries", deletedCount);
        }
    }
}
