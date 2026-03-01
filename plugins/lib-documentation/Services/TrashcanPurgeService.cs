using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Documentation.Services;

/// <summary>
/// Background service that periodically purges expired trashcan entries
/// across all documentation namespaces. Without this service, expired entries
/// are only cleaned lazily when ListTrashcan is called.
/// </summary>
public class TrashcanPurgeService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TrashcanPurgeService> _logger;
    private readonly DocumentationServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    private const string ALL_NAMESPACES_KEY = "all-namespaces";
    private const string BINDINGS_REGISTRY_KEY = "repo-bindings";
    private const string TRASH_KEY_PREFIX = "trash:";

    /// <summary>
    /// Creates a new instance of the TrashcanPurgeService.
    /// </summary>
    public TrashcanPurgeService(
        IServiceProvider serviceProvider,
        ILogger<TrashcanPurgeService> logger,
        DocumentationServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration, nameof(configuration));
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));

        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Gets the check interval from configuration.
    /// </summary>
    private TimeSpan CheckInterval => TimeSpan.FromMinutes(_configuration.TrashcanPurgeCheckIntervalMinutes);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.documentation", "TrashcanPurgeService.ExecuteAsync");

        if (!_configuration.TrashcanPurgeEnabled)
        {
            _logger.LogInformation("Trashcan purge service is disabled");
            return;
        }

        _logger.LogInformation("Trashcan purge service starting, check interval: {Interval}", CheckInterval);

        // Wait before first check to allow other services to start
        try
        {
            await Task.Delay(CheckInterval, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Trashcan purge service cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeExpiredEntriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during trashcan purge cycle");
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

        _logger.LogInformation("Trashcan purge service stopped");
    }

    /// <summary>
    /// Iterates all namespaces and purges expired trashcan entries.
    /// </summary>
    private async Task PurgeExpiredEntriesAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.documentation", "TrashcanPurgeService.PurgeExpiredEntriesAsync");

        _logger.LogDebug("Starting trashcan purge cycle");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();

        // Discover all namespaces (union of global registry + repo bindings)
        var namespaces = await DiscoverNamespacesAsync(stateStoreFactory, cancellationToken);

        if (namespaces.Count == 0)
        {
            _logger.LogDebug("No namespaces to check for expired trashcan entries");
            return;
        }

        var totalPurged = 0;

        foreach (var namespaceId in namespaces)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var purgedCount = await PurgeNamespaceTrashcanAsync(
                    stateStoreFactory, namespaceId, cancellationToken);
                totalPurged += purgedCount;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Error purging trashcan for namespace {Namespace}", namespaceId);
            }
        }

        if (totalPurged > 0)
        {
            _logger.LogInformation(
                "Trashcan purge cycle complete: purged {Count} expired entries across {NamespaceCount} namespaces",
                totalPurged, namespaces.Count);
        }
        else
        {
            _logger.LogDebug("Trashcan purge cycle complete: no expired entries found");
        }
    }

    /// <summary>
    /// Discovers all known namespaces from global registry and repo bindings.
    /// </summary>
    private async Task<HashSet<string>> DiscoverNamespacesAsync(
        IStateStoreFactory stateStoreFactory, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.documentation", "TrashcanPurgeService.DiscoverNamespacesAsync");

        var allNamespaces = new HashSet<string>();

        var stringSetStore = stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Documentation);

        var globalNamespaces = await stringSetStore.GetAsync(ALL_NAMESPACES_KEY, cancellationToken);
        if (globalNamespaces != null)
        {
            foreach (var ns in globalNamespaces)
            {
                allNamespaces.Add(ns);
            }
        }

        var bindingNamespaces = await stringSetStore.GetAsync(BINDINGS_REGISTRY_KEY, cancellationToken);
        if (bindingNamespaces != null)
        {
            foreach (var ns in bindingNamespaces)
            {
                allNamespaces.Add(ns);
            }
        }

        return allNamespaces;
    }

    /// <summary>
    /// Purges expired trashcan entries for a single namespace.
    /// Returns the number of entries purged.
    /// </summary>
    private async Task<int> PurgeNamespaceTrashcanAsync(
        IStateStoreFactory stateStoreFactory, string namespaceId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.documentation", "TrashcanPurgeService.PurgeNamespaceTrashcanAsync");

        var guidListStore = stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Documentation);
        var trashStore = stateStoreFactory.GetStore<DocumentationService.TrashedDocument>(StateStoreDefinitions.Documentation);

        var trashListKey = $"ns-trash:{namespaceId}";
        var (trashedDocIds, trashEtag) = await guidListStore.GetWithETagAsync(trashListKey, cancellationToken);

        if (trashedDocIds == null || trashedDocIds.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var expiredIds = new List<Guid>();
        var expiredKeysToDelete = new List<string>();

        foreach (var docId in trashedDocIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var trashKey = $"{TRASH_KEY_PREFIX}{namespaceId}:{docId}";
            var trashedDoc = await trashStore.GetAsync(trashKey, cancellationToken);

            if (trashedDoc == null)
            {
                // Stale index entry — mark for cleanup
                expiredIds.Add(docId);
                continue;
            }

            if (trashedDoc.ExpiresAt < now)
            {
                expiredIds.Add(docId);
                expiredKeysToDelete.Add(trashKey);
            }
        }

        if (expiredIds.Count == 0)
        {
            return 0;
        }

        // Delete expired trash entries
        if (expiredKeysToDelete.Count > 0)
        {
            await trashStore.DeleteBulkAsync(expiredKeysToDelete, cancellationToken);
        }

        // Update the trashcan index — remove expired IDs
        trashedDocIds.RemoveAll(id => expiredIds.Contains(id));

        if (trashedDocIds.Count == 0)
        {
            await guidListStore.DeleteAsync(trashListKey, cancellationToken);
        }
        else
        {
            // GetWithETagAsync returns non-null etag when key exists (loaded above);
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await guidListStore.TrySaveAsync(
                trashListKey, trashedDocIds, trashEtag ?? string.Empty, cancellationToken);

            if (saveResult == null)
            {
                // Concurrent modification — will retry next cycle
                _logger.LogDebug(
                    "Trashcan index concurrent modification for namespace {Namespace}, will retry next cycle",
                    namespaceId);
                return 0;
            }
        }

        _logger.LogDebug(
            "Purged {Count} expired trashcan entries for namespace {Namespace}",
            expiredIds.Count, namespaceId);

        return expiredIds.Count;
    }

    /// <summary>
    /// Tries to publish an error event for unexpected failures.
    /// </summary>
    private async Task TryPublishErrorAsync(Exception ex, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.documentation", "TrashcanPurgeService.TryPublishErrorAsync");
        try
        {
            using var errorScope = _serviceProvider.CreateScope();
            var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
            await messageBus.TryPublishErrorAsync(
                "documentation",
                "TrashcanPurge",
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
