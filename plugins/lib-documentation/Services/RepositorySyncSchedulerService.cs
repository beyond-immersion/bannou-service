using BeyondImmersion.BannouService.Documentation.Models;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Documentation.Services;

/// <summary>
/// Background service that periodically checks for repository bindings
/// that need synchronization and triggers sync operations.
/// </summary>
public class RepositorySyncSchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RepositorySyncSchedulerService> _logger;
    private readonly DocumentationServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    private const string BINDINGS_REGISTRY_KEY = "repo-bindings";
    private const string BINDING_KEY_PREFIX = "repo-binding:";

    /// <summary>
    /// Creates a new instance of the RepositorySyncSchedulerService.
    /// </summary>
    public RepositorySyncSchedulerService(
        IServiceProvider serviceProvider,
        ILogger<RepositorySyncSchedulerService> logger,
        DocumentationServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Gets the check interval from configuration or defaults to 5 minutes.
    /// </summary>
    private TimeSpan CheckInterval => TimeSpan.FromMinutes(_configuration.SyncSchedulerCheckIntervalMinutes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "RepositorySyncSchedulerService.ExecuteAsync");
        // Check if scheduler is enabled
        if (!_configuration.SyncSchedulerEnabled)
        {
            _logger.LogInformation("Repository sync scheduler is disabled");
            return;
        }

        _logger.LogInformation("Repository sync scheduler starting, check interval: {Interval}", CheckInterval);

        // Wait a bit before first check to allow other services to start
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_configuration.RepositorySyncCheckIntervalSeconds), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Repository sync scheduler cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessScheduledSyncsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled sync check");
                await TryPublishErrorAsync(ex, stoppingToken);
            }

            try
            {
                await CleanupStaleRepositoriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during stale repository cleanup");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Repository sync scheduler stopped");
    }

    /// <summary>
    /// Checks for bindings that need sync and triggers sync operations.
    /// </summary>
    private async Task ProcessScheduledSyncsAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "RepositorySyncSchedulerService.ProcessScheduledSyncsAsync");
        _logger.LogDebug("Checking for bindings that need sync");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var documentationService = scope.ServiceProvider.GetRequiredService<IDocumentationService>();

        // Get all binding namespace IDs from registry
        var registryStore = stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Documentation);
        var bindingNamespaces = await registryStore.GetAsync(BINDINGS_REGISTRY_KEY, cancellationToken);

        if (bindingNamespaces == null || bindingNamespaces.Count == 0)
        {
            _logger.LogDebug("No repository bindings to check");
            return;
        }

        var bindingStore = stateStoreFactory.GetStore<RepositoryBinding>(StateStoreDefinitions.Documentation);
        var now = DateTimeOffset.UtcNow;
        var syncCount = 0;
        var maxConcurrent = _configuration.MaxConcurrentSyncs;

        foreach (var namespaceId in bindingNamespaces)
        {
            if (syncCount >= maxConcurrent)
            {
                _logger.LogDebug("Max concurrent syncs ({Max}) reached, remaining bindings will be processed next cycle", maxConcurrent);
                break;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bindingKey = $"{BINDING_KEY_PREFIX}{namespaceId}";
                var binding = await bindingStore.GetAsync(bindingKey, cancellationToken);

                if (binding == null)
                {
                    continue;
                }

                // Skip if sync not enabled or binding is in error/disabled state
                if (!binding.SyncEnabled)
                {
                    continue;
                }

                if (binding.Status == BindingStatusInternal.Disabled || binding.Status == BindingStatusInternal.Syncing)
                {
                    continue;
                }

                // Check if it's time to sync
                var needsSync = false;

                if (binding.NextSyncAt.HasValue)
                {
                    needsSync = binding.NextSyncAt.Value <= now;
                }
                else if (binding.LastSyncAt.HasValue)
                {
                    // Calculate next sync from last sync + interval
                    var nextSyncTime = binding.LastSyncAt.Value.AddMinutes(binding.SyncIntervalMinutes);
                    needsSync = nextSyncTime <= now;
                }
                else
                {
                    // Never synced, needs initial sync
                    needsSync = true;
                }

                if (needsSync)
                {
                    _logger.LogInformation("Triggering scheduled sync for namespace {Namespace}", namespaceId);

                    // Trigger sync via the service
                    var (status, _) = await documentationService.SyncRepositoryAsync(
                        new SyncRepositoryRequest
                        {
                            Namespace = namespaceId,
                            Force = false
                        },
                        cancellationToken);

                    if (status == StatusCodes.OK)
                    {
                        syncCount++;
                        _logger.LogInformation("Scheduled sync completed for namespace {Namespace}", namespaceId);
                    }
                    else
                    {
                        _logger.LogWarning("Scheduled sync failed for namespace {Namespace}, status: {Status}", namespaceId, status);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing binding for namespace {Namespace}", namespaceId);
            }
        }

        if (syncCount > 0)
        {
            _logger.LogInformation("Completed {Count} scheduled sync(s) this cycle", syncCount);
        }
        else
        {
            _logger.LogDebug("No bindings needed sync this cycle");
        }
    }

    /// <summary>
    /// Cleans up stale repository directories that are no longer bound or haven't synced
    /// within the configured GitStorageCleanupHours threshold.
    /// </summary>
    private async Task CleanupStaleRepositoriesAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "RepositorySyncSchedulerService.CleanupStaleRepositoriesAsync");
        var storagePath = _configuration.GitStoragePath;
        if (!Directory.Exists(storagePath))
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var gitSyncService = scope.ServiceProvider.GetRequiredService<IGitSyncService>();

        // Get all known binding IDs
        var registryStore = stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Documentation);
        var bindingNamespaces = await registryStore.GetAsync(BINDINGS_REGISTRY_KEY, cancellationToken) ?? [];

        var bindingStore = stateStoreFactory.GetStore<RepositoryBinding>(StateStoreDefinitions.Documentation);

        // Build a set of valid binding IDs from the registry
        var validBindingIds = new HashSet<string>();
        foreach (var ns in bindingNamespaces)
        {
            var bindingKey = $"{BINDING_KEY_PREFIX}{ns}";
            var binding = await bindingStore.GetAsync(bindingKey, cancellationToken);
            if (binding != null)
            {
                validBindingIds.Add(binding.BindingId.ToString());
            }
        }

        var cleanupThreshold = DateTimeOffset.UtcNow.AddHours(-_configuration.GitStorageCleanupHours);

        foreach (var repoDir in Directory.GetDirectories(storagePath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(repoDir);

            // Only consider directories that look like GUIDs (binding IDs)
            if (!Guid.TryParse(dirName, out _))
            {
                continue;
            }

            // If the binding still exists, skip cleanup
            if (validBindingIds.Contains(dirName))
            {
                continue;
            }

            // Orphaned repo directory (no binding) or stale - check last modification time
            var dirInfo = new DirectoryInfo(repoDir);
            if (dirInfo.LastWriteTimeUtc < cleanupThreshold.UtcDateTime)
            {
                _logger.LogInformation("Cleaning up stale repository directory: {Path}", repoDir);
                await gitSyncService.CleanupRepositoryAsync(repoDir, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Tries to publish an error event.
    /// </summary>
    private async Task TryPublishErrorAsync(Exception ex, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "RepositorySyncSchedulerService.TryPublishErrorAsync");
        try
        {
            using var errorScope = _serviceProvider.CreateScope();
            var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
            await messageBus.TryPublishErrorAsync(
                "documentation",
                "ScheduledSync",
                ex.GetType().Name,
                ex.Message,
                severity: ServiceErrorEventSeverity.Error,
                cancellationToken: cancellationToken);
        }
        catch
        {
            // Don't let error publishing failures affect the loop
        }
    }
}
