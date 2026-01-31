#nullable enable

using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NRedisStack;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using NRedisStack.Search.Literals.Enums;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// Configuration for the state store factory.
/// </summary>
public class StateStoreFactoryConfiguration
{
    /// <summary>
    /// Use in-memory storage for all stores. Data is NOT persisted.
    /// ONLY use for testing or minimal infrastructure scenarios.
    /// </summary>
    public bool UseInMemory { get; set; }

    /// <summary>
    /// Redis connection string.
    /// </summary>
    public string RedisConnectionString { get; set; } = "bannou-redis:6379";

    /// <summary>
    /// MySQL connection string.
    /// </summary>
    public string? MySqlConnectionString { get; set; }

    /// <summary>
    /// Maximum number of connection retry attempts for databases.
    /// </summary>
    public int ConnectionRetryCount { get; set; } = 10;

    /// <summary>
    /// Total timeout in seconds for database connection attempts.
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Store configurations by name.
    /// </summary>
    public Dictionary<string, StoreConfiguration> Stores { get; set; } = new();
}

/// <summary>
/// Factory for creating typed state stores.
/// Manages Redis connections and MySQL DbContext options.
/// </summary>
public sealed class StateStoreFactory : IStateStoreFactory, IAsyncDisposable
{
    private readonly StateStoreFactoryConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<StateStoreFactory> _logger;
    private readonly ITelemetryProvider? _telemetryProvider;

    private ConnectionMultiplexer? _redis;
    private DbContextOptions<StateDbContext>? _mysqlOptions;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    // Cache for created store instances
    private readonly ConcurrentDictionary<string, object> _storeCache = new();

    /// <summary>
    /// Creates a new StateStoreFactory.
    /// </summary>
    /// <param name="configuration">Factory configuration.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="telemetryProvider">Optional telemetry provider for instrumentation.</param>
    public StateStoreFactory(
        StateStoreFactoryConfiguration configuration,
        ILoggerFactory loggerFactory,
        ITelemetryProvider? telemetryProvider = null)
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<StateStoreFactory>();
        _telemetryProvider = telemetryProvider;

        if (_telemetryProvider != null)
        {
            _logger.LogDebug(
                "StateStoreFactory created with telemetry instrumentation: tracing={TracingEnabled}, metrics={MetricsEnabled}",
                _telemetryProvider.TracingEnabled, _telemetryProvider.MetricsEnabled);
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            // Skip real infrastructure when using in-memory mode
            if (_configuration.UseInMemory)
            {
                _logger.LogWarning(
                    "State store factory using IN-MEMORY mode. Data will NOT be persisted across restarts!");
                _initialized = true;
                return;
            }

            // Initialize Redis if any store uses it
            var hasRedisStore = _configuration.Stores.Values.Any(s => s.Backend == StateBackend.Redis);
            if (hasRedisStore && !string.IsNullOrEmpty(_configuration.RedisConnectionString))
            {
                _logger.LogDebug("Connecting to Redis: {ConnectionString}",
                    _configuration.RedisConnectionString.Split(',')[0]); // Log only host
                _redis = await ConnectionMultiplexer.ConnectAsync(_configuration.RedisConnectionString);

                // Auto-create search indexes for stores with EnableSearch=true
                await CreateSearchIndexesAsync();
            }

            // Initialize MySQL if any store uses it (with retry logic)
            var hasMySqlStore = _configuration.Stores.Values.Any(s => s.Backend == StateBackend.MySql);
            if (hasMySqlStore && !string.IsNullOrEmpty(_configuration.MySqlConnectionString))
            {
                var maxRetries = _configuration.ConnectionRetryCount;
                var totalTimeoutSeconds = _configuration.ConnectionTimeoutSeconds;
                var retryDelayMs = Math.Max(1000, (totalTimeoutSeconds * 1000) / Math.Max(1, maxRetries));

                _logger.LogDebug(
                    "Initializing MySQL connection (timeout: {TotalTimeout}s, retries: {MaxRetries})",
                    totalTimeoutSeconds, maxRetries);

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        _logger.LogDebug(
                            "Attempting MySQL connection (attempt {Attempt}/{MaxAttempts})",
                            attempt, maxRetries);

                        var optionsBuilder = new DbContextOptionsBuilder<StateDbContext>();
                        optionsBuilder.UseMySql(
                            _configuration.MySqlConnectionString,
                            ServerVersion.AutoDetect(_configuration.MySqlConnectionString));

                        // Store options for per-operation context creation (thread-safe)
                        _mysqlOptions = optionsBuilder.Options;

                        // Test connection with a temporary context - dispose immediately
                        using var testContext = new StateDbContext(_mysqlOptions);
                        await testContext.Database.EnsureCreatedAsync();

                        _logger.LogInformation("MySQL connection established successfully");
                        break; // Success - exit retry loop
                    }
                    catch (Exception ex) when (attempt < maxRetries)
                    {
                        _logger.LogWarning(
                            "MySQL connection failed (attempt {Attempt}/{MaxAttempts}): {Message}",
                            attempt, maxRetries, ex.Message);

                        _mysqlOptions = null;
                        await Task.Delay(retryDelayMs);
                    }
                    // Last attempt - let exception propagate
                }

                if (_mysqlOptions == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to establish MySQL connection after {maxRetries} attempts");
                }
            }

            _initialized = true;
            _logger.LogInformation("State store factory initialized with {Count} stores", _configuration.Stores.Count);
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
    }

    /// <inheritdoc/>
    public async Task<IStateStore<TValue>> GetStoreAsync<TValue>(string storeName, CancellationToken cancellationToken = default)
        where TValue : class
    {

        if (!HasStore(storeName))
        {
            throw new InvalidOperationException($"Store '{storeName}' is not configured");
        }

        // Ensure connections are initialized asynchronously
        if (!_initialized)
        {
            await EnsureInitializedAsync();
        }

        return GetStoreInternal<TValue>(storeName);
    }

    /// <inheritdoc/>
    public IStateStore<TValue> GetStore<TValue>(string storeName)
        where TValue : class
    {

        if (!HasStore(storeName))
        {
            throw new InvalidOperationException($"Store '{storeName}' is not configured");
        }

        // Ensure connections are initialized before accessing the cache.
        // The double-checked locking in EnsureInitializedAsync makes subsequent calls fast.
        // This MUST happen outside GetOrAdd to avoid blocking inside the callback.
        // WARNING: This is sync-over-async - prefer GetStoreAsync() or call InitializeAsync() at startup.
        if (!_initialized)
        {
            _logger.LogWarning(
                "GetStore called before InitializeAsync - performing sync-over-async initialization. " +
                "Consider calling InitializeAsync() at startup or using GetStoreAsync().");
            EnsureInitializedAsync().GetAwaiter().GetResult();
        }

        return GetStoreInternal<TValue>(storeName);
    }

    private IStateStore<TValue> GetStoreInternal<TValue>(string storeName)
        where TValue : class
    {
        var cacheKey = $"{storeName}:{typeof(TValue).FullName}";

        return (IStateStore<TValue>)_storeCache.GetOrAdd(cacheKey, _ =>
        {
            IStateStore<TValue> store;
            string backend;

            // Use in-memory store when configured (for testing/minimal infrastructure)
            if (_configuration.UseInMemory)
            {
                var memoryLogger = _loggerFactory.CreateLogger<InMemoryStateStore<TValue>>();
                store = new InMemoryStateStore<TValue>(storeName, memoryLogger);
                backend = "memory";
            }
            else
            {
                var storeConfig = _configuration.Stores[storeName];

                if (storeConfig.Backend == StateBackend.Redis)
                {
                    if (_redis == null)
                    {
                        throw new InvalidOperationException("Redis connection not available");
                    }

                    var keyPrefix = storeConfig.KeyPrefix ?? storeName;
                    var defaultTtl = storeConfig.DefaultTtlSeconds.HasValue
                        ? TimeSpan.FromSeconds(storeConfig.DefaultTtlSeconds.Value)
                        : (TimeSpan?)null;

                    // Use searchable store if search is enabled
                    if (storeConfig.EnableSearch)
                    {
                        var searchLogger = _loggerFactory.CreateLogger<RedisSearchStateStore<TValue>>();
                        store = new RedisSearchStateStore<TValue>(
                            _redis.GetDatabase(),
                            keyPrefix,
                            defaultTtl,
                            searchLogger);
                    }
                    else
                    {
                        var redisLogger = _loggerFactory.CreateLogger<RedisStateStore<TValue>>();
                        store = new RedisStateStore<TValue>(
                            _redis.GetDatabase(),
                            keyPrefix,
                            defaultTtl,
                            redisLogger);
                    }
                    backend = "redis";
                }
                else if (storeConfig.Backend == StateBackend.Memory)
                {
                    var memoryLogger = _loggerFactory.CreateLogger<InMemoryStateStore<TValue>>();
                    store = new InMemoryStateStore<TValue>(storeName, memoryLogger);
                    backend = "memory";
                }
                else // MySql
                {
                    if (_mysqlOptions == null)
                    {
                        throw new InvalidOperationException("MySQL connection not available");
                    }

                    var mysqlLogger = _loggerFactory.CreateLogger<MySqlStateStore<TValue>>();
                    store = new MySqlStateStore<TValue>(
                        _mysqlOptions,
                        storeConfig.TableName ?? storeName,
                        mysqlLogger);
                    backend = "mysql";
                }
            }

            // Wrap with telemetry instrumentation if available
            if (_telemetryProvider != null)
            {
                store = _telemetryProvider.WrapStateStore(store, storeName, backend);
            }

            return store;
        });
    }

    /// <inheritdoc/>
    public IQueryableStateStore<TValue> GetQueryableStore<TValue>(string storeName)
        where TValue : class
    {

        if (!HasStore(storeName))
        {
            throw new InvalidOperationException($"Store '{storeName}' is not configured");
        }

        var storeConfig = _configuration.Stores[storeName];
        if (storeConfig.Backend != StateBackend.MySql)
        {
            throw new InvalidOperationException(
                $"Store '{storeName}' uses {storeConfig.Backend} backend which does not support queries. " +
                "Use GetStore<T>() for Redis stores or configure a MySQL store for queries.");
        }

        var store = GetStore<TValue>(storeName);
        return (IQueryableStateStore<TValue>)store;
    }

    /// <inheritdoc/>
    public IJsonQueryableStateStore<TValue> GetJsonQueryableStore<TValue>(string storeName)
        where TValue : class
    {

        if (!HasStore(storeName))
        {
            throw new InvalidOperationException($"Store '{storeName}' is not configured");
        }

        var storeConfig = _configuration.Stores[storeName];
        if (storeConfig.Backend != StateBackend.MySql)
        {
            throw new InvalidOperationException(
                $"Store '{storeName}' uses {storeConfig.Backend} backend which does not support JSON queries. " +
                "Only MySQL stores support efficient JSON path queries.");
        }

        var store = GetStore<TValue>(storeName);
        return (IJsonQueryableStateStore<TValue>)store;
    }

    /// <inheritdoc/>
    public ISearchableStateStore<TValue> GetSearchableStore<TValue>(string storeName)
        where TValue : class
    {

        if (!HasStore(storeName))
        {
            throw new InvalidOperationException($"Store '{storeName}' is not configured");
        }

        var storeConfig = _configuration.Stores[storeName];
        if (storeConfig.Backend != StateBackend.Redis)
        {
            throw new InvalidOperationException(
                $"Store '{storeName}' uses {storeConfig.Backend} backend which does not support search. " +
                "Only Redis stores with EnableSearch=true support full-text search.");
        }

        if (!storeConfig.EnableSearch)
        {
            throw new InvalidOperationException(
                $"Store '{storeName}' does not have search enabled. " +
                "Set EnableSearch=true in the store configuration to use full-text search.");
        }

        var store = GetStore<TValue>(storeName);
        return (ISearchableStateStore<TValue>)store;
    }

    /// <inheritdoc/>
    public bool SupportsSearch(string storeName)
    {

        if (!_configuration.Stores.TryGetValue(storeName, out var config))
        {
            return false;
        }

        return config.Backend == StateBackend.Redis && config.EnableSearch;
    }

    /// <inheritdoc/>
    public bool HasStore(string storeName)
    {
        return _configuration.Stores.ContainsKey(storeName);
    }

    /// <inheritdoc/>
    public StateBackend GetBackendType(string storeName)
    {

        // In-memory mode overrides all backends
        if (_configuration.UseInMemory)
        {
            return StateBackend.Memory;
        }

        if (!_configuration.Stores.TryGetValue(storeName, out var config))
        {
            throw new InvalidOperationException($"Store '{storeName}' is not configured");
        }

        return config.Backend;
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetStoreNames()
    {
        return _configuration.Stores.Keys;
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetStoreNames(StateBackend backend)
    {
        return _configuration.Stores
            .Where(kvp => kvp.Value.Backend == backend)
            .Select(kvp => kvp.Key);
    }

    /// <summary>
    /// Creates search indexes for all stores with EnableSearch=true.
    /// Uses a generic schema that indexes common JSON fields.
    /// </summary>
    private async Task CreateSearchIndexesAsync()
    {
        if (_redis == null) return;

        var searchStores = _configuration.Stores
            .Where(kvp => kvp.Value.Backend == StateBackend.Redis && kvp.Value.EnableSearch)
            .ToList();

        if (searchStores.Count == 0) return;

        var db = _redis.GetDatabase();
        var ft = db.FT();

        foreach (var (storeName, config) in searchStores)
        {
            var indexName = $"{storeName}-idx";
            var keyPrefix = config.KeyPrefix ?? storeName;

            try
            {
                // Check if index already exists
                try
                {
                    await ft.InfoAsync(indexName);
                    _logger.LogDebug("Search index '{Index}' already exists", indexName);
                    continue;
                }
                catch (RedisServerException ex) when (ex.Message.Contains("Unknown index") || ex.Message.Contains("no such index"))
                {
                    // Index doesn't exist - create it
                }

                // Create a generic schema that indexes common JSON fields as TEXT
                // This allows basic full-text search on any JSON documents stored in the store
                var schema = new Schema()
                    .AddTextField(new FieldName("$.*", "content"), weight: 1.0, sortable: false, noStem: false);

                var ftParams = new FTCreateParams()
                    .On(IndexDataType.JSON)
                    .Prefix($"{keyPrefix}:");

                await ft.CreateAsync(indexName, ftParams, schema);
                _logger.LogInformation("Created search index '{Index}' for store '{Store}' with prefix '{Prefix}'",
                    indexName, storeName, keyPrefix);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create search index '{Index}' for store '{Store}' - search queries may fail",
                    indexName, storeName);
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_redis != null)
        {
            await _redis.DisposeAsync();
            _redis = null;
        }

        // Note: _mysqlOptions doesn't need disposal - it's just configuration.
        // Each MySqlStateStore creates and disposes its own DbContext per operation.
        _mysqlOptions = null;

        _storeCache.Clear();
        _initLock.Dispose();
    }
}
