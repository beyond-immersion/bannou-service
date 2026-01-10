using BeyondImmersion.BannouService.Documentation.Models;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

    private const string STATE_STORE = "documentation-statestore";
    private const string BINDINGS_REGISTRY_KEY = "repo-bindings";
    private const string BINDING_KEY_PREFIX = "repo-binding:";

    /// <summary>
    /// Creates a new instance of the RepositorySyncSchedulerService.
    /// </summary>
    public RepositorySyncSchedulerService(
        IServiceProvider serviceProvider,
        ILogger<RepositorySyncSchedulerService> logger,
        DocumentationServiceConfiguration configuration)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Gets the check interval from configuration or defaults to 5 minutes.
    /// </summary>
    private TimeSpan CheckInterval => TimeSpan.FromMinutes(_configuration.SyncSchedulerCheckIntervalMinutes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
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
        _logger.LogDebug("Checking for bindings that need sync");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var documentationService = scope.ServiceProvider.GetRequiredService<IDocumentationService>();

        // Get all binding namespace IDs from registry
        var registryStore = stateStoreFactory.GetStore<HashSet<string>>(STATE_STORE);
        var bindingNamespaces = await registryStore.GetAsync(BINDINGS_REGISTRY_KEY, cancellationToken);

        if (bindingNamespaces == null || bindingNamespaces.Count == 0)
        {
            _logger.LogDebug("No repository bindings to check");
            return;
        }

        var bindingStore = stateStoreFactory.GetStore<RepositoryBinding>(STATE_STORE);
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
    /// Tries to publish an error event.
    /// </summary>
    private async Task TryPublishErrorAsync(Exception ex, CancellationToken cancellationToken)
    {
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
