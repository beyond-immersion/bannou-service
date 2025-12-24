#nullable enable

using BeyondImmersion.BannouService.State.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// Configuration for a single state store.
/// </summary>
public class StoreConfiguration
{
    /// <summary>
    /// Backend type for this store.
    /// </summary>
    public StateBackend Backend { get; set; } = StateBackend.Redis;

    /// <summary>
    /// Key prefix for namespacing (Redis only).
    /// </summary>
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// Default TTL in seconds (Redis only).
    /// </summary>
    public int? DefaultTtlSeconds { get; set; }

    /// <summary>
    /// Table name (MySQL only, defaults to store name).
    /// </summary>
    public string? TableName { get; set; }
}

/// <summary>
/// Configuration for the state store factory.
/// </summary>
public class StateStoreFactoryConfiguration
{
    /// <summary>
    /// Redis connection string.
    /// </summary>
    public string RedisConnectionString { get; set; } = "bannou-redis:6379";

    /// <summary>
    /// MySQL connection string.
    /// </summary>
    public string? MySqlConnectionString { get; set; }

    /// <summary>
    /// Store configurations by name.
    /// </summary>
    public Dictionary<string, StoreConfiguration> Stores { get; set; } = new();
}

/// <summary>
/// Factory for creating typed state stores.
/// Manages Redis connections and MySQL DbContext.
/// </summary>
public sealed class StateStoreFactory : IStateStoreFactory, IAsyncDisposable
{
    private readonly StateStoreFactoryConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<StateStoreFactory> _logger;

    private ConnectionMultiplexer? _redis;
    private StateDbContext? _mysqlContext;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    // Cache for created store instances
    private readonly ConcurrentDictionary<string, object> _storeCache = new();

    /// <summary>
    /// Creates a new StateStoreFactory.
    /// </summary>
    /// <param name="configuration">Factory configuration.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    public StateStoreFactory(
        StateStoreFactoryConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<StateStoreFactory>();
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            // Initialize Redis if any store uses it
            var hasRedisStore = _configuration.Stores.Values.Any(s => s.Backend == StateBackend.Redis);
            if (hasRedisStore && !string.IsNullOrEmpty(_configuration.RedisConnectionString))
            {
                _logger.LogInformation("Connecting to Redis: {ConnectionString}",
                    _configuration.RedisConnectionString.Split(',')[0]); // Log only host
                _redis = await ConnectionMultiplexer.ConnectAsync(_configuration.RedisConnectionString);
            }

            // Initialize MySQL if any store uses it
            var hasMySqlStore = _configuration.Stores.Values.Any(s => s.Backend == StateBackend.MySql);
            if (hasMySqlStore && !string.IsNullOrEmpty(_configuration.MySqlConnectionString))
            {
                _logger.LogInformation("Initializing MySQL connection");
                var optionsBuilder = new DbContextOptionsBuilder<StateDbContext>();
                optionsBuilder.UseMySql(
                    _configuration.MySqlConnectionString,
                    ServerVersion.AutoDetect(_configuration.MySqlConnectionString));
                _mysqlContext = new StateDbContext(optionsBuilder.Options);

                // Ensure database and tables exist
                await _mysqlContext.Database.EnsureCreatedAsync();
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
    public IStateStore<TValue> GetStore<TValue>(string storeName)
        where TValue : class
    {
        ArgumentNullException.ThrowIfNull(storeName);

        if (!HasStore(storeName))
        {
            throw new InvalidOperationException($"Store '{storeName}' is not configured");
        }

        var cacheKey = $"{storeName}:{typeof(TValue).FullName}";

        return (IStateStore<TValue>)_storeCache.GetOrAdd(cacheKey, _ =>
        {
            // Ensure connections are initialized
            EnsureInitializedAsync().GetAwaiter().GetResult();

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

                var redisLogger = _loggerFactory.CreateLogger<RedisStateStore<TValue>>();
                return new RedisStateStore<TValue>(
                    _redis.GetDatabase(),
                    keyPrefix,
                    defaultTtl,
                    redisLogger);
            }
            else // MySql
            {
                if (_mysqlContext == null)
                {
                    throw new InvalidOperationException("MySQL connection not available");
                }

                var mysqlLogger = _loggerFactory.CreateLogger<MySqlStateStore<TValue>>();
                return new MySqlStateStore<TValue>(
                    _mysqlContext,
                    storeConfig.TableName ?? storeName,
                    mysqlLogger);
            }
        });
    }

    /// <inheritdoc/>
    public IQueryableStateStore<TValue> GetQueryableStore<TValue>(string storeName)
        where TValue : class
    {
        ArgumentNullException.ThrowIfNull(storeName);

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
    public bool HasStore(string storeName)
    {
        ArgumentNullException.ThrowIfNull(storeName);
        return _configuration.Stores.ContainsKey(storeName);
    }

    /// <inheritdoc/>
    public StateBackend GetBackendType(string storeName)
    {
        ArgumentNullException.ThrowIfNull(storeName);

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

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_redis != null)
        {
            await _redis.DisposeAsync();
            _redis = null;
        }

        if (_mysqlContext != null)
        {
            await _mysqlContext.DisposeAsync();
            _mysqlContext = null;
        }

        _storeCache.Clear();
        _initLock.Dispose();
    }
}
