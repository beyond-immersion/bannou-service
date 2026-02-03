using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;

namespace BeyondImmersion.BannouService.State.Tests;

/// <summary>
/// Tests verifying the state store interface hierarchy is correctly implemented.
/// This ensures that:
/// - ISearchableStateStore extends ICacheableStateStore (not a sibling)
/// - ICacheableStateStore extends IStateStore
/// - Factory returns properly typed stores
/// - Type casting works as expected across the hierarchy
///
/// These tests validate the architectural fix where ISearchableStateStore was
/// changed from extending IStateStore directly to extending ICacheableStateStore.
/// </summary>
public class StateStoreInterfaceHierarchyTests : IAsyncDisposable
{
    private readonly StateStoreFactory _factory;
    private readonly string _uniqueStoreName;

    public StateStoreInterfaceHierarchyTests()
    {
        // Use unique store names to avoid test pollution
        _uniqueStoreName = $"test-store-{Guid.NewGuid():N}";

        var configuration = new StateStoreFactoryConfiguration
        {
            UseInMemory = true,
            Stores = new Dictionary<string, StoreConfiguration>
            {
                [_uniqueStoreName] = new StoreConfiguration
                {
                    Backend = StateBackend.Redis // Will use InMemory since UseInMemory=true
                }
            }
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        var telemetryProvider = new NullTelemetryProvider();

        _factory = new StateStoreFactory(configuration, loggerFactory, telemetryProvider);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    #region Interface Hierarchy Tests

    /// <summary>
    /// Verifies ISearchableStateStore extends ICacheableStateStore at the type level.
    /// This is a compile-time constraint but we verify it at runtime via reflection
    /// to catch any accidental interface hierarchy changes.
    /// </summary>
    [Fact]
    public void ISearchableStateStore_ExtendsCacheableStateStore()
    {
        // Act
        var searchableInterface = typeof(ISearchableStateStore<>);
        var cacheableInterface = typeof(ICacheableStateStore<>);

        // Get the interface's base interfaces
        var baseInterfaces = searchableInterface.GetInterfaces();

        // Assert - ISearchableStateStore should extend ICacheableStateStore
        Assert.Contains(baseInterfaces, i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == cacheableInterface);
    }

    /// <summary>
    /// Verifies ICacheableStateStore extends IStateStore at the type level.
    /// </summary>
    [Fact]
    public void ICacheableStateStore_ExtendsStateStore()
    {
        // Act
        var cacheableInterface = typeof(ICacheableStateStore<>);
        var baseInterface = typeof(IStateStore<>);

        // Get the interface's base interfaces
        var baseInterfaces = cacheableInterface.GetInterfaces();

        // Assert - ICacheableStateStore should extend IStateStore
        Assert.Contains(baseInterfaces, i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == baseInterface);
    }

    /// <summary>
    /// Verifies the complete interface hierarchy: ISearchableStateStore → ICacheableStateStore → IStateStore.
    /// </summary>
    [Fact]
    public void InterfaceHierarchy_IsCorrectlyOrdered()
    {
        // The hierarchy should be: ISearchableStateStore extends ICacheableStateStore extends IStateStore
        var searchableType = typeof(ISearchableStateStore<object>);
        var cacheableType = typeof(ICacheableStateStore<object>);
        var baseType = typeof(IStateStore<object>);

        // ISearchableStateStore should be assignable to ICacheableStateStore
        Assert.True(cacheableType.IsAssignableFrom(searchableType),
            "ISearchableStateStore should be assignable to ICacheableStateStore");

        // ICacheableStateStore should be assignable to IStateStore
        Assert.True(baseType.IsAssignableFrom(cacheableType),
            "ICacheableStateStore should be assignable to IStateStore");

        // Therefore, ISearchableStateStore should also be assignable to IStateStore
        Assert.True(baseType.IsAssignableFrom(searchableType),
            "ISearchableStateStore should be assignable to IStateStore (transitively)");
    }

    #endregion

    #region Implementation Type Tests

    /// <summary>
    /// Verifies InMemoryStateStore implements ICacheableStateStore.
    /// </summary>
    [Fact]
    public void InMemoryStateStore_ImplementsCacheableStateStore()
    {
        // Act
        var inMemoryType = typeof(InMemoryStateStore<>);
        var cacheableType = typeof(ICacheableStateStore<>);

        // Get all interfaces implemented by InMemoryStateStore
        var implementedInterfaces = inMemoryType.GetInterfaces();

        // Assert
        Assert.Contains(implementedInterfaces, i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == cacheableType);
    }

    /// <summary>
    /// Verifies that an InMemoryStateStore instance can be cast to ICacheableStateStore.
    /// </summary>
    [Fact]
    public void InMemoryStateStore_Instance_CastsToCacheableStateStore()
    {
        // Arrange
        var logger = new Mock<ILogger<InMemoryStateStore<TestEntity>>>();
        var store = new InMemoryStateStore<TestEntity>("cast-test-store", logger.Object);

        // Act & Assert - Should not throw
        var cacheableStore = store as ICacheableStateStore<TestEntity>;
        Assert.NotNull(cacheableStore);
    }

    /// <summary>
    /// Verifies that an InMemoryStateStore instance can be cast to IStateStore.
    /// </summary>
    [Fact]
    public void InMemoryStateStore_Instance_CastsToStateStore()
    {
        // Arrange
        var logger = new Mock<ILogger<InMemoryStateStore<TestEntity>>>();
        var store = new InMemoryStateStore<TestEntity>("cast-test-store", logger.Object);

        // Act & Assert - Should not throw
        var baseStore = store as IStateStore<TestEntity>;
        Assert.NotNull(baseStore);
    }

    #endregion

    #region Factory GetStore Tests

    /// <summary>
    /// Verifies GetStore returns a store that implements IStateStore.
    /// </summary>
    [Fact]
    public async Task GetStore_ReturnsStateStore()
    {
        // Act
        var store = await _factory.GetStoreAsync<TestEntity>(_uniqueStoreName);

        // Assert
        Assert.NotNull(store);
        Assert.IsAssignableFrom<IStateStore<TestEntity>>(store);
    }

    /// <summary>
    /// Verifies GetCacheableStore returns a store that implements ICacheableStateStore.
    /// </summary>
    [Fact]
    public async Task GetCacheableStore_ReturnsCacheableStateStore()
    {
        // Act
        var store = await _factory.GetCacheableStoreAsync<TestEntity>(_uniqueStoreName);

        // Assert
        Assert.NotNull(store);
        Assert.IsAssignableFrom<ICacheableStateStore<TestEntity>>(store);
    }

    /// <summary>
    /// Verifies GetCacheableStore returns a store that is also assignable to IStateStore
    /// (because ICacheableStateStore extends IStateStore).
    /// </summary>
    [Fact]
    public async Task GetCacheableStore_AlsoAssignableToStateStore()
    {
        // Act
        var cacheableStore = await _factory.GetCacheableStoreAsync<TestEntity>(_uniqueStoreName);

        // Assert - Should be castable to IStateStore
        var baseStore = cacheableStore as IStateStore<TestEntity>;
        Assert.NotNull(baseStore);
    }

    #endregion

    #region Cacheable Operations Through Factory Tests

    /// <summary>
    /// Verifies that set operations work on stores retrieved through GetCacheableStore.
    /// </summary>
    [Fact]
    public async Task GetCacheableStore_SetOperations_Work()
    {
        // Arrange
        var store = await _factory.GetCacheableStoreAsync<TestEntity>(_uniqueStoreName);
        var item = new TestEntity { Id = "1", Name = "Test", Value = 42 };

        // Act
        var added = await store.AddToSetAsync("test-set", item);
        var contains = await store.SetContainsAsync("test-set", item);
        var count = await store.SetCountAsync("test-set");

        // Assert
        Assert.True(added);
        Assert.True(contains);
        Assert.Equal(1, count);

        // Cleanup
        await store.DeleteSetAsync("test-set");
    }

    /// <summary>
    /// Verifies that sorted set operations work on stores retrieved through GetCacheableStore.
    /// </summary>
    [Fact]
    public async Task GetCacheableStore_SortedSetOperations_Work()
    {
        // Arrange
        var store = await _factory.GetCacheableStoreAsync<TestEntity>(_uniqueStoreName);

        // Act
        var added = await store.SortedSetAddAsync("test-zset", "member1", 100.0);
        var score = await store.SortedSetScoreAsync("test-zset", "member1");
        var count = await store.SortedSetCountAsync("test-zset");

        // Assert
        Assert.True(added);
        Assert.Equal(100.0, score);
        Assert.Equal(1, count);

        // Cleanup
        await store.SortedSetDeleteAsync("test-zset");
    }

    /// <summary>
    /// Verifies that counter operations work on stores retrieved through GetCacheableStore.
    /// </summary>
    [Fact]
    public async Task GetCacheableStore_CounterOperations_Work()
    {
        // Arrange
        var store = await _factory.GetCacheableStoreAsync<TestEntity>(_uniqueStoreName);

        // Act
        var incremented = await store.IncrementAsync("test-counter", 5);
        var value = await store.GetCounterAsync("test-counter");
        var decremented = await store.DecrementAsync("test-counter", 2);

        // Assert
        Assert.Equal(5, incremented);
        Assert.Equal(5, value);
        Assert.Equal(3, decremented);

        // Cleanup
        await store.DeleteCounterAsync("test-counter");
    }

    /// <summary>
    /// Verifies that hash operations work on stores retrieved through GetCacheableStore.
    /// </summary>
    [Fact]
    public async Task GetCacheableStore_HashOperations_Work()
    {
        // Arrange
        var store = await _factory.GetCacheableStoreAsync<TestEntity>(_uniqueStoreName);

        // Act
        var set = await store.HashSetAsync("test-hash", "field1", "value1");
        var value = await store.HashGetAsync<string>("test-hash", "field1");
        var exists = await store.HashExistsAsync("test-hash", "field1");
        var count = await store.HashCountAsync("test-hash");

        // Assert
        Assert.True(set);
        Assert.Equal("value1", value);
        Assert.True(exists);
        Assert.Equal(1, count);

        // Cleanup
        await store.DeleteHashAsync("test-hash");
    }

    #endregion

    #region Type Checking Pattern Tests

    /// <summary>
    /// Verifies the pattern used in StateStoreFactory: checking if a store is ISearchableStateStore first.
    /// This ensures that searchable stores are correctly identified before cacheable stores.
    /// </summary>
    [Fact]
    public void TypeCheckingPattern_SearchableBeforeCacheable_Works()
    {
        // Arrange - Create a mock that implements both interfaces (simulating RedisSearchStateStore)
        var mockSearchable = new Mock<ISearchableStateStore<TestEntity>>();

        // The mock object should be identifiable as both interfaces
        var store = mockSearchable.Object;

        // Act & Assert - Pattern: check searchable first
        if (store is ISearchableStateStore<TestEntity> searchableStore)
        {
            // This branch should be taken for searchable stores
            Assert.NotNull(searchableStore);

            // A searchable store should also be assignable to ICacheableStateStore
            Assert.True(store is ICacheableStateStore<TestEntity>,
                "ISearchableStateStore should also be ICacheableStateStore");
        }
        else if (store is ICacheableStateStore<TestEntity>)
        {
            Assert.Fail("Searchable store was misidentified as only cacheable");
        }
        else
        {
            Assert.Fail("Store was not identified as searchable or cacheable");
        }
    }

    /// <summary>
    /// Verifies that a cacheable-only store (like InMemoryStateStore) is correctly identified
    /// as cacheable but NOT searchable.
    /// </summary>
    [Fact]
    public void TypeCheckingPattern_CacheableNotSearchable_Works()
    {
        // Arrange
        var logger = new Mock<ILogger<InMemoryStateStore<TestEntity>>>();
        IStateStore<TestEntity> store = new InMemoryStateStore<TestEntity>("type-check-store", logger.Object);

        // Act & Assert - Pattern: check searchable first
        if (store is ISearchableStateStore<TestEntity>)
        {
            Assert.Fail("InMemoryStateStore should NOT be ISearchableStateStore");
        }
        else if (store is ICacheableStateStore<TestEntity> cacheableStore)
        {
            // This branch should be taken for InMemoryStateStore
            Assert.NotNull(cacheableStore);
        }
        else
        {
            Assert.Fail("InMemoryStateStore should be ICacheableStateStore");
        }
    }

    #endregion

    /// <summary>
    /// Test entity for storage tests.
    /// </summary>
    public class TestEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}
