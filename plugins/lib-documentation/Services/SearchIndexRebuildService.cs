#nullable enable

using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Documentation.Services;

/// <summary>
/// Background service that rebuilds the in-memory search index on startup.
/// Iterates all known namespaces and populates the search index from state store data.
/// Runs once on startup then exits, ensuring search works immediately after restart.
/// </summary>
public class SearchIndexRebuildService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SearchIndexRebuildService> _logger;
    private readonly DocumentationServiceConfiguration _configuration;

    private const string ALL_NAMESPACES_KEY = "all-namespaces";
    private const string BINDINGS_REGISTRY_KEY = "repo-bindings";

    /// <summary>
    /// Creates a new SearchIndexRebuildService.
    /// </summary>
    public SearchIndexRebuildService(
        IServiceProvider serviceProvider,
        ILogger<SearchIndexRebuildService> logger,
        DocumentationServiceConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.SearchIndexRebuildOnStartup)
        {
            _logger.LogInformation("Search index rebuild on startup is disabled");
            return;
        }

        // Wait briefly to allow state store infrastructure to initialize
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        _logger.LogInformation("Starting search index rebuild");

        try
        {
            await RebuildAllNamespacesAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Search index rebuild cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during search index rebuild");
        }
    }

    /// <summary>
    /// Discovers all namespaces and rebuilds the search index for each.
    /// Uses both the global namespace registry and bindings registry for discovery.
    /// </summary>
    private async Task RebuildAllNamespacesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var searchIndexService = scope.ServiceProvider.GetRequiredService<ISearchIndexService>();

        // Collect namespaces from both registries for complete coverage
        var allNamespaces = new HashSet<string>();

        var stringSetStore = stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Documentation);

        // Global namespace registry (populated by document creation)
        var globalNamespaces = await stringSetStore.GetAsync(ALL_NAMESPACES_KEY, cancellationToken);
        if (globalNamespaces != null)
        {
            foreach (var ns in globalNamespaces)
            {
                allNamespaces.Add(ns);
            }
        }

        // Bindings registry (populated by repo binding creation)
        var bindingNamespaces = await stringSetStore.GetAsync(BINDINGS_REGISTRY_KEY, cancellationToken);
        if (bindingNamespaces != null)
        {
            foreach (var ns in bindingNamespaces)
            {
                allNamespaces.Add(ns);
            }
        }

        if (allNamespaces.Count == 0)
        {
            _logger.LogInformation("No namespaces found for search index rebuild");
            return;
        }

        _logger.LogInformation("Rebuilding search index for {Count} namespace(s)", allNamespaces.Count);

        var totalDocuments = 0;
        foreach (var namespaceId in allNamespaces)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var indexed = await searchIndexService.RebuildIndexAsync(namespaceId, cancellationToken);
                totalDocuments += indexed;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to rebuild search index for namespace {Namespace}", namespaceId);
            }
        }

        _logger.LogInformation("Search index rebuild complete: {Documents} documents across {Namespaces} namespace(s)",
            totalDocuments, allNamespaces.Count);
    }
}
