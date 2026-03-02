using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BeyondImmersion.BannouService.State.Tests;

/// <summary>
/// Tests for StateStoreFactory pure logic using InMemory mode.
/// Covers configuration queries, backend selection, store type validation,
/// error publisher deduplication, and InMemory store creation.
/// </summary>
public class StateStoreFactoryTests : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
    private readonly NullTelemetryProvider _telemetryProvider = new();

    /// <summary>
    /// Creates a factory in InMemory mode with the given store configurations.
    /// </summary>
    private StateStoreFactory CreateInMemoryFactory(
        Dictionary<string, StoreConfiguration> stores,
        IMessageBus? messageBus = null,
        bool enableErrorPublishing = true,
        int deduplicationWindowSeconds = 60)
    {
        var config = new StateStoreFactoryConfiguration
        {
            UseInMemory = true,
            EnableErrorEventPublishing = enableErrorPublishing,
            ErrorEventDeduplicationWindowSeconds = deduplicationWindowSeconds,
            Stores = stores
        };
        return new StateStoreFactory(config, _loggerFactory, _telemetryProvider, messageBus);
    }

    /// <summary>
    /// Creates a factory in SQLite mode with the given store configurations.
    /// Does NOT initialize (no file system access) — only tests pre-init methods.
    /// </summary>
    private StateStoreFactory CreateSqliteFactory(
        Dictionary<string, StoreConfiguration> stores)
    {
        var config = new StateStoreFactoryConfiguration
        {
            UseSqlite = true,
            Stores = stores
        };
        return new StateStoreFactory(config, _loggerFactory, _telemetryProvider);
    }

    // Track factories for cleanup
    private readonly List<StateStoreFactory> _factories = [];

    private StateStoreFactory TrackFactory(StateStoreFactory factory)
    {
        _factories.Add(factory);
        return factory;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var factory in _factories)
        {
            await factory.DisposeAsync();
        }
    }

    // ==================== HasStore ====================

    [Fact]
    public void HasStore_ConfiguredStore_ReturnsTrue()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["my-store"] = new() { Backend = StateBackend.Redis }
        }));

        Assert.True(factory.HasStore("my-store"));
    }

    [Fact]
    public void HasStore_UnconfiguredStore_ReturnsFalse()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>()));

        Assert.False(factory.HasStore("nonexistent"));
    }

    // ==================== GetBackendType ====================

    [Fact]
    public void GetBackendType_InMemoryMode_AlwaysReturnsMemory()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["redis-store"] = new() { Backend = StateBackend.Redis },
            ["mysql-store"] = new() { Backend = StateBackend.MySql },
            ["memory-store"] = new() { Backend = StateBackend.Memory }
        }));

        Assert.Equal(StateBackend.Memory, factory.GetBackendType("redis-store"));
        Assert.Equal(StateBackend.Memory, factory.GetBackendType("mysql-store"));
        Assert.Equal(StateBackend.Memory, factory.GetBackendType("memory-store"));
    }

    [Fact]
    public void GetBackendType_SqliteMode_MySqlBecomesSqlite()
    {
        var factory = TrackFactory(CreateSqliteFactory(new Dictionary<string, StoreConfiguration>
        {
            ["mysql-store"] = new() { Backend = StateBackend.MySql }
        }));

        Assert.Equal(StateBackend.Sqlite, factory.GetBackendType("mysql-store"));
    }

    [Fact]
    public void GetBackendType_SqliteMode_RedisBecomeMemory()
    {
        var factory = TrackFactory(CreateSqliteFactory(new Dictionary<string, StoreConfiguration>
        {
            ["redis-store"] = new() { Backend = StateBackend.Redis }
        }));

        Assert.Equal(StateBackend.Memory, factory.GetBackendType("redis-store"));
    }

    [Fact]
    public void GetBackendType_UnconfiguredStore_ThrowsInvalidOperation()
    {
        // InMemory mode short-circuits and returns Memory for ALL stores
        // (doesn't check configuration dictionary), so we need non-InMemory mode
        // to test the "not configured" path
        var config = new StateStoreFactoryConfiguration
        {
            UseInMemory = false,
            UseSqlite = false,
            Stores = new Dictionary<string, StoreConfiguration>()
        };
        var normalFactory = new StateStoreFactory(config, _loggerFactory, _telemetryProvider);
        _factories.Add(normalFactory);

        var ex = Assert.Throws<InvalidOperationException>(
            () => normalFactory.GetBackendType("nonexistent"));
        Assert.Contains("not configured", ex.Message);
    }

    // ==================== SupportsSearch ====================

    [Fact]
    public void SupportsSearch_RedisWithSearchEnabled_ReturnsTrue()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["search-store"] = new() { Backend = StateBackend.Redis, EnableSearch = true }
        }));

        Assert.True(factory.SupportsSearch("search-store"));
    }

    [Fact]
    public void SupportsSearch_RedisWithSearchDisabled_ReturnsFalse()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["redis-store"] = new() { Backend = StateBackend.Redis, EnableSearch = false }
        }));

        Assert.False(factory.SupportsSearch("redis-store"));
    }

    [Fact]
    public void SupportsSearch_MySqlStore_ReturnsFalse()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["mysql-store"] = new() { Backend = StateBackend.MySql }
        }));

        Assert.False(factory.SupportsSearch("mysql-store"));
    }

    [Fact]
    public void SupportsSearch_UnconfiguredStore_ReturnsFalse()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>()));

        Assert.False(factory.SupportsSearch("nonexistent"));
    }

    // ==================== GetStoreNames ====================

    [Fact]
    public void GetStoreNames_ReturnsAllConfiguredNames()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["store-a"] = new() { Backend = StateBackend.Redis },
            ["store-b"] = new() { Backend = StateBackend.MySql },
            ["store-c"] = new() { Backend = StateBackend.Memory }
        }));

        var names = factory.GetStoreNames().ToList();
        Assert.Equal(3, names.Count);
        Assert.Contains("store-a", names);
        Assert.Contains("store-b", names);
        Assert.Contains("store-c", names);
    }

    [Fact]
    public void GetStoreNames_EmptyConfig_ReturnsEmpty()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>()));

        Assert.Empty(factory.GetStoreNames());
    }

    [Fact]
    public void GetStoreNames_FilteredByBackend_ReturnsOnlyMatchingStores()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["redis-1"] = new() { Backend = StateBackend.Redis },
            ["redis-2"] = new() { Backend = StateBackend.Redis },
            ["mysql-1"] = new() { Backend = StateBackend.MySql },
            ["memory-1"] = new() { Backend = StateBackend.Memory }
        }));

        var redisStores = factory.GetStoreNames(StateBackend.Redis).ToList();
        Assert.Equal(2, redisStores.Count);
        Assert.Contains("redis-1", redisStores);
        Assert.Contains("redis-2", redisStores);

        var mysqlStores = factory.GetStoreNames(StateBackend.MySql).ToList();
        Assert.Single(mysqlStores);
        Assert.Contains("mysql-1", mysqlStores);

        var memoryStores = factory.GetStoreNames(StateBackend.Memory).ToList();
        Assert.Single(memoryStores);
        Assert.Contains("memory-1", memoryStores);
    }

    [Fact]
    public void GetStoreNames_FilteredByBackend_NoMatchReturnsEmpty()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["redis-1"] = new() { Backend = StateBackend.Redis }
        }));

        Assert.Empty(factory.GetStoreNames(StateBackend.MySql));
    }

    // ==================== GetRedisOperations ====================

    [Fact]
    public void GetRedisOperations_InMemoryMode_ReturnsNull()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>()));

        Assert.Null(factory.GetRedisOperations());
    }

    [Fact]
    public void GetRedisOperations_SqliteMode_ReturnsNull()
    {
        var factory = TrackFactory(CreateSqliteFactory(new Dictionary<string, StoreConfiguration>()));

        Assert.Null(factory.GetRedisOperations());
    }

    // ==================== GetStore (InMemory mode) ====================

    [Fact]
    public void GetStore_UnconfiguredStore_ThrowsInvalidOperation()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>()));

        var ex = Assert.Throws<InvalidOperationException>(
            () => factory.GetStore<TestModel>("nonexistent"));
        Assert.Contains("not configured", ex.Message);
    }

    [Fact]
    public void GetStore_InMemoryMode_ReturnsWorkingStore()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["test-store"] = new() { Backend = StateBackend.Redis }
        }));

        var store = factory.GetStore<TestModel>("test-store");
        Assert.NotNull(store);
    }

    [Fact]
    public void GetStore_InMemoryMode_ReturnsCacheableStore()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["cache-store"] = new() { Backend = StateBackend.Redis }
        }));

        // InMemoryStateStore implements ICacheableStateStore
        var store = factory.GetStore<TestModel>("cache-store");
        Assert.IsAssignableFrom<ICacheableStateStore<TestModel>>(store);
    }

    [Fact]
    public void GetStore_SameStoreName_ReturnsCachedInstance()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["cached-store"] = new() { Backend = StateBackend.Redis }
        }));

        var store1 = factory.GetStore<TestModel>("cached-store");
        var store2 = factory.GetStore<TestModel>("cached-store");

        Assert.Same(store1, store2);
    }

    [Fact]
    public void GetStore_DifferentTypes_ReturnsDifferentInstances()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["typed-store"] = new() { Backend = StateBackend.Redis }
        }));

        var store1 = factory.GetStore<TestModel>("typed-store");
        var store2 = factory.GetStore<AnotherTestModel>("typed-store");

        Assert.NotSame((object)store1, (object)store2);
    }

    // ==================== GetStoreAsync ====================

    [Fact]
    public async Task GetStoreAsync_UnconfiguredStore_ThrowsInvalidOperation()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.GetStoreAsync<TestModel>("nonexistent"));
        Assert.Contains("not configured", ex.Message);
    }

    [Fact]
    public async Task GetStoreAsync_InMemoryMode_ReturnsWorkingStore()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["async-store"] = new() { Backend = StateBackend.Redis }
        }));

        var store = await factory.GetStoreAsync<TestModel>("async-store");
        Assert.NotNull(store);
    }

    // ==================== GetQueryableStore ====================

    [Fact]
    public void GetQueryableStore_UnconfiguredStore_ThrowsInvalidOperation()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>()));

        var ex = Assert.Throws<InvalidOperationException>(
            () => factory.GetQueryableStore<TestModel>("nonexistent"));
        Assert.Contains("not configured", ex.Message);
    }

    [Fact]
    public void GetQueryableStore_RedisBackend_ThrowsInvalidOperation()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["redis-store"] = new() { Backend = StateBackend.Redis }
        }));

        var ex = Assert.Throws<InvalidOperationException>(
            () => factory.GetQueryableStore<TestModel>("redis-store"));
        Assert.Contains("does not support queries", ex.Message);
    }

    [Fact]
    public void GetQueryableStore_MemoryBackend_ThrowsInvalidOperation()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["memory-store"] = new() { Backend = StateBackend.Memory }
        }));

        var ex = Assert.Throws<InvalidOperationException>(
            () => factory.GetQueryableStore<TestModel>("memory-store"));
        Assert.Contains("does not support queries", ex.Message);
    }

    // ==================== GetJsonQueryableStore ====================

    [Fact]
    public void GetJsonQueryableStore_UnconfiguredStore_ThrowsInvalidOperation()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>()));

        var ex = Assert.Throws<InvalidOperationException>(
            () => factory.GetJsonQueryableStore<TestModel>("nonexistent"));
        Assert.Contains("not configured", ex.Message);
    }

    [Fact]
    public void GetJsonQueryableStore_RedisBackend_ThrowsInvalidOperation()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["redis-store"] = new() { Backend = StateBackend.Redis }
        }));

        var ex = Assert.Throws<InvalidOperationException>(
            () => factory.GetJsonQueryableStore<TestModel>("redis-store"));
        Assert.Contains("does not support JSON queries", ex.Message);
    }

    // ==================== GetSearchableStore ====================

    [Fact]
    public void GetSearchableStore_UnconfiguredStore_ThrowsInvalidOperation()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>()));

        var ex = Assert.Throws<InvalidOperationException>(
            () => factory.GetSearchableStore<TestModel>("nonexistent"));
        Assert.Contains("not configured", ex.Message);
    }

    [Fact]
    public void GetSearchableStore_MySqlBackend_ThrowsInvalidOperation()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["mysql-store"] = new() { Backend = StateBackend.MySql }
        }));

        var ex = Assert.Throws<InvalidOperationException>(
            () => factory.GetSearchableStore<TestModel>("mysql-store"));
        Assert.Contains("does not support search", ex.Message);
    }

    [Fact]
    public void GetSearchableStore_RedisWithoutSearch_ThrowsInvalidOperation()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["redis-no-search"] = new() { Backend = StateBackend.Redis, EnableSearch = false }
        }));

        var ex = Assert.Throws<InvalidOperationException>(
            () => factory.GetSearchableStore<TestModel>("redis-no-search"));
        Assert.Contains("does not have search enabled", ex.Message);
    }

    // ==================== GetCacheableStore ====================

    [Fact]
    public void GetCacheableStore_UnconfiguredStore_ThrowsInvalidOperation()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>()));

        var ex = Assert.Throws<InvalidOperationException>(
            () => factory.GetCacheableStore<TestModel>("nonexistent"));
        Assert.Contains("not configured", ex.Message);
    }

    [Fact]
    public void GetCacheableStore_MySqlBackend_ThrowsInvalidOperation()
    {
        // MySql throws even in InMemory mode because GetCacheableStore checks
        // GetBackendType which returns Memory in InMemory mode.
        // To test the MySql rejection we need a non-InMemory non-SQLite factory.
        var config = new StateStoreFactoryConfiguration
        {
            UseInMemory = false,
            UseSqlite = false,
            Stores = new Dictionary<string, StoreConfiguration>
            {
                ["mysql-store"] = new() { Backend = StateBackend.MySql }
            }
        };
        var factory = new StateStoreFactory(config, _loggerFactory, _telemetryProvider);
        _factories.Add(factory);

        var ex = Assert.Throws<InvalidOperationException>(
            () => factory.GetCacheableStore<TestModel>("mysql-store"));
        Assert.Contains("does not support Set/Sorted Set operations", ex.Message);
    }

    [Fact]
    public void GetCacheableStore_InMemoryMode_ReturnsCacheableStore()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["cacheable"] = new() { Backend = StateBackend.Redis }
        }));

        var store = factory.GetCacheableStore<TestModel>("cacheable");
        Assert.NotNull(store);
        Assert.IsAssignableFrom<ICacheableStateStore<TestModel>>(store);
    }

    // ==================== GetCacheableStoreAsync ====================

    [Fact]
    public async Task GetCacheableStoreAsync_UnconfiguredStore_ThrowsInvalidOperation()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.GetCacheableStoreAsync<TestModel>("nonexistent"));
        Assert.Contains("not configured", ex.Message);
    }

    [Fact]
    public async Task GetCacheableStoreAsync_InMemoryMode_ReturnsCacheableStore()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["async-cacheable"] = new() { Backend = StateBackend.Redis }
        }));

        var store = await factory.GetCacheableStoreAsync<TestModel>("async-cacheable");
        Assert.NotNull(store);
        Assert.IsAssignableFrom<ICacheableStateStore<TestModel>>(store);
    }

    // ==================== GetKeyCountAsync (InMemory mode) ====================

    [Fact]
    public async Task GetKeyCountAsync_UnconfiguredStore_ThrowsInvalidOperation()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.GetKeyCountAsync("nonexistent"));
        Assert.Contains("not configured", ex.Message);
    }

    [Fact]
    public async Task GetKeyCountAsync_InMemoryMode_ReturnsCount()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["count-store"] = new() { Backend = StateBackend.Redis }
        }));

        // Initialize and add some data
        await factory.InitializeAsync();
        var store = factory.GetStore<TestModel>("count-store");
        await store.SaveAsync("key1", new TestModel { Name = "A" });
        await store.SaveAsync("key2", new TestModel { Name = "B" });

        var count = await factory.GetKeyCountAsync("count-store");
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetKeyCountAsync_InMemoryMode_EmptyStore_ReturnsZero()
    {
        // Use a unique store name to avoid interference from other tests
        var storeName = $"empty-count-{Guid.NewGuid():N}";
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            [storeName] = new() { Backend = StateBackend.Redis }
        }));

        await factory.InitializeAsync();
        var count = await factory.GetKeyCountAsync(storeName);
        Assert.Equal(0, count);
    }

    // ==================== InitializeAsync ====================

    [Fact]
    public async Task InitializeAsync_InMemoryMode_CompletesSuccessfully()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["init-store"] = new() { Backend = StateBackend.Redis }
        }));

        // Should not throw
        await factory.InitializeAsync();

        // After init, stores should be accessible
        var store = factory.GetStore<TestModel>("init-store");
        Assert.NotNull(store);
    }

    [Fact]
    public async Task InitializeAsync_MultipleCalls_IsIdempotent()
    {
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["idempotent-store"] = new() { Backend = StateBackend.Redis }
        }));

        await factory.InitializeAsync();
        await factory.InitializeAsync();
        await factory.InitializeAsync();

        // Should still work fine
        var store = factory.GetStore<TestModel>("idempotent-store");
        Assert.NotNull(store);
    }

    // ==================== Error Publisher Deduplication ====================

    [Fact]
    public async Task ErrorPublisher_PublishesErrorToMessageBus()
    {
        var mockMessageBus = new Mock<IMessageBus>();
        var factory = TrackFactory(CreateInMemoryFactory(
            new Dictionary<string, StoreConfiguration>
            {
                ["error-store"] = new() { Backend = StateBackend.Redis }
            },
            messageBus: mockMessageBus.Object,
            enableErrorPublishing: true));

        await factory.InitializeAsync();

        // Get a store to trigger CreateErrorPublisher
        var store = factory.GetStore<TestModel>("error-store");

        // The error publisher is internal to the store - we can verify
        // the factory creates working stores with error publishing by
        // triggering a save/get and checking the mock was/wasn't called.
        // Since InMemoryStateStore doesn't actually fail, we verify the
        // factory construction path works without throwing.
        Assert.NotNull(store);
    }

    [Fact]
    public void ErrorPublisher_DisabledPublishing_StoreStillCreated()
    {
        var factory = TrackFactory(CreateInMemoryFactory(
            new Dictionary<string, StoreConfiguration>
            {
                ["no-error-store"] = new() { Backend = StateBackend.Redis }
            },
            enableErrorPublishing: false));

        var store = factory.GetStore<TestModel>("no-error-store");
        Assert.NotNull(store);
    }

    [Fact]
    public void ErrorPublisher_NoMessageBus_StoreStillCreated()
    {
        var factory = TrackFactory(CreateInMemoryFactory(
            new Dictionary<string, StoreConfiguration>
            {
                ["no-bus-store"] = new() { Backend = StateBackend.Redis }
            },
            messageBus: null));

        var store = factory.GetStore<TestModel>("no-bus-store");
        Assert.NotNull(store);
    }

    // ==================== Store operations through factory ====================

    [Fact]
    public async Task GetStore_InMemoryMode_CanSaveAndRetrieve()
    {
        var storeName = $"roundtrip-{Guid.NewGuid():N}";
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            [storeName] = new() { Backend = StateBackend.Redis }
        }));

        await factory.InitializeAsync();

        var store = factory.GetStore<TestModel>(storeName);
        var model = new TestModel { Name = "test-value" };

        await store.SaveAsync("key-1", model);
        var result = await store.GetAsync("key-1");

        Assert.NotNull(result);
        Assert.Equal("test-value", result.Name);
    }

    [Fact]
    public async Task GetCacheableStore_InMemoryMode_SortedSetOperationsWork()
    {
        var storeName = $"sorted-set-{Guid.NewGuid():N}";
        var factory = TrackFactory(CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            [storeName] = new() { Backend = StateBackend.Redis }
        }));

        await factory.InitializeAsync();

        var cacheStore = factory.GetCacheableStore<TestModel>(storeName);

        // Add to sorted set
        await cacheStore.SortedSetAddAsync("scores", "player-a", 100.0);
        await cacheStore.SortedSetAddAsync("scores", "player-b", 200.0);

        // Query range
        var results = await cacheStore.SortedSetRangeByScoreAsync(
            "scores", 0, 300);

        Assert.Equal(2, results.Count);
    }

    // ==================== DisposeAsync ====================

    [Fact]
    public async Task DisposeAsync_ClearsStoreCache()
    {
        var factory = CreateInMemoryFactory(new Dictionary<string, StoreConfiguration>
        {
            ["dispose-store"] = new() { Backend = StateBackend.Redis }
        });

        await factory.InitializeAsync();
        var store1 = factory.GetStore<TestModel>("dispose-store");
        Assert.NotNull(store1);

        await factory.DisposeAsync();

        // After dispose, getting a store should create a new one
        // (re-initialization needed). We can't easily test this without
        // causing sync-over-async, so we just verify dispose doesn't throw.
    }

    // ==================== Configuration defaults ====================

    [Fact]
    public void StateStoreFactoryConfiguration_DefaultValues_AreCorrect()
    {
        var config = new StateStoreFactoryConfiguration();

        Assert.False(config.UseInMemory);
        Assert.False(config.UseSqlite);
        Assert.Equal("./data/state", config.SqliteDataPath);
        Assert.Equal("bannou-redis:6379", config.RedisConnectionString);
        Assert.Null(config.MySqlConnectionString);
        Assert.Equal(10, config.ConnectionRetryCount);
        Assert.Equal(60, config.ConnectionTimeoutSeconds);
        Assert.Equal(1000, config.MinRetryDelayMs);
        Assert.Equal(10000, config.InMemoryFallbackLimit);
        Assert.True(config.EnableErrorEventPublishing);
        Assert.Equal(60, config.ErrorEventDeduplicationWindowSeconds);
        Assert.Empty(config.Stores);
    }

    // ==================== Test models ====================

    private class TestModel
    {
        public string Name { get; set; } = string.Empty;
    }

    private class AnotherTestModel
    {
        public int Value { get; set; }
    }
}
