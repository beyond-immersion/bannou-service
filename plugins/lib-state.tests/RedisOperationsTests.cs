using BeyondImmersion.BannouService.Services;
using StackExchange.Redis;

namespace BeyondImmersion.BannouService.State.Tests;

/// <summary>
/// Unit tests for IRedisOperations interface and related functionality.
/// These tests verify the interface contract and StateStoreFactory integration.
///
/// Note: Full integration testing of RedisOperations requires a live Redis connection
/// and is performed in infrastructure-tests, not unit tests.
/// </summary>
public class RedisOperationsTests
{
    #region Interface Contract Tests

    /// <summary>
    /// Documents the expected signature of ScriptEvaluateAsync for Lua script execution.
    /// This is the primary escape hatch for atomic Redis operations like distributed locks.
    /// </summary>
    [Fact]
    public void ScriptEvaluateAsync_InterfaceSignature_IsCorrect()
    {
        // This test documents the interface contract
        var interfaceType = typeof(IRedisOperations);

        var method = interfaceType.GetMethod("ScriptEvaluateAsync");
        Assert.NotNull(method);

        var parameters = method.GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.Equal("script", parameters[0].Name);
        Assert.Equal(typeof(RedisKey[]), parameters[1].ParameterType);
        Assert.Equal(typeof(RedisValue[]), parameters[2].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[3].ParameterType);

        Assert.Equal(typeof(Task<RedisResult>), method.ReturnType);
    }

    /// <summary>
    /// Documents the expected signature of atomic counter operations.
    /// </summary>
    [Fact]
    public void AtomicCounterMethods_InterfaceSignature_IsCorrect()
    {
        var interfaceType = typeof(IRedisOperations);

        // IncrementAsync
        var incrementMethod = interfaceType.GetMethod("IncrementAsync");
        Assert.NotNull(incrementMethod);
        Assert.Equal(typeof(Task<long>), incrementMethod.ReturnType);

        // DecrementAsync
        var decrementMethod = interfaceType.GetMethod("DecrementAsync");
        Assert.NotNull(decrementMethod);
        Assert.Equal(typeof(Task<long>), decrementMethod.ReturnType);
    }

    /// <summary>
    /// Documents the expected hash operation methods.
    /// </summary>
    [Fact]
    public void HashOperationMethods_InterfaceSignature_IsCorrect()
    {
        var interfaceType = typeof(IRedisOperations);

        // HashGetAsync
        Assert.NotNull(interfaceType.GetMethod("HashGetAsync"));

        // HashSetAsync (two overloads)
        var hashSetMethods = interfaceType.GetMethods().Where(m => m.Name == "HashSetAsync").ToList();
        Assert.Equal(2, hashSetMethods.Count);

        // HashDeleteAsync
        Assert.NotNull(interfaceType.GetMethod("HashDeleteAsync"));

        // HashIncrementAsync
        Assert.NotNull(interfaceType.GetMethod("HashIncrementAsync"));

        // HashGetAllAsync
        Assert.NotNull(interfaceType.GetMethod("HashGetAllAsync"));
    }

    /// <summary>
    /// Documents the expected TTL operation methods.
    /// </summary>
    [Fact]
    public void TtlOperationMethods_InterfaceSignature_IsCorrect()
    {
        var interfaceType = typeof(IRedisOperations);

        // ExpireAsync
        Assert.NotNull(interfaceType.GetMethod("ExpireAsync"));

        // TimeToLiveAsync
        Assert.NotNull(interfaceType.GetMethod("TimeToLiveAsync"));

        // PersistAsync
        Assert.NotNull(interfaceType.GetMethod("PersistAsync"));
    }

    /// <summary>
    /// Documents the expected key operation methods.
    /// </summary>
    [Fact]
    public void KeyOperationMethods_InterfaceSignature_IsCorrect()
    {
        var interfaceType = typeof(IRedisOperations);

        // KeyExistsAsync
        Assert.NotNull(interfaceType.GetMethod("KeyExistsAsync"));

        // KeyDeleteAsync
        Assert.NotNull(interfaceType.GetMethod("KeyDeleteAsync"));
    }

    /// <summary>
    /// Documents the GetDatabase method for direct IDatabase access.
    /// </summary>
    [Fact]
    public void GetDatabase_InterfaceSignature_IsCorrect()
    {
        var interfaceType = typeof(IRedisOperations);

        var method = interfaceType.GetMethod("GetDatabase");
        Assert.NotNull(method);
        Assert.Equal(typeof(IDatabase), method.ReturnType);
        Assert.Empty(method.GetParameters());
    }

    #endregion

    #region StateStoreFactory Integration Tests

    /// <summary>
    /// Verifies that GetRedisOperations is available on IStateStoreFactory interface.
    /// </summary>
    [Fact]
    public void IStateStoreFactory_HasGetRedisOperationsMethod()
    {
        var factoryType = typeof(IStateStoreFactory);

        var method = factoryType.GetMethod("GetRedisOperations");
        Assert.NotNull(method);

        // Return type is nullable IRedisOperations
        Assert.True(method.ReturnType == typeof(IRedisOperations));
    }

    #endregion

    #region Usage Pattern Documentation

    /// <summary>
    /// Documents the expected usage pattern for distributed locks using Lua scripts.
    /// This is the primary use case for IRedisOperations in Bannou.
    /// </summary>
    [Fact]
    public void DistributedLock_UnlockScript_PatternDocumented()
    {
        // The unlock script pattern used by RedisDistributedLockProvider:
        // - Atomically check if we own the lock (value starts with our owner prefix)
        // - Only delete if we own it (prevents releasing someone else's lock)
        var unlockScript = @"
            local value = redis.call('GET', KEYS[1])
            if value and string.find(value, ARGV[1], 1, true) == 1 then
                return redis.call('DEL', KEYS[1])
            end
            return 0
        ";

        // The script expects:
        // - KEYS[1]: The lock key
        // - ARGV[1]: The lock owner prefix to match
        //
        // Returns:
        // - 1 if lock was released
        // - 0 if lock was not released (not owned by us or expired)

        Assert.Contains("GET", unlockScript);
        Assert.Contains("DEL", unlockScript);
        Assert.Contains("string.find", unlockScript);
        Assert.Contains("true", unlockScript); // plain=true for literal matching
    }

    /// <summary>
    /// Documents the expected usage pattern for atomic counters.
    /// Common use cases: rate limiting, sequence generation, statistics.
    /// </summary>
    [Fact]
    public void AtomicCounter_UsagePattern_Documented()
    {
        // Atomic counters are useful for:
        // 1. Rate limiting: INCR and check value
        // 2. Sequence generation: INCR and use result as ID
        // 3. Statistics: INCRBY to add values atomically
        // 4. Distributed counting: Multiple instances can increment safely

        // Example rate limiting pattern:
        // var count = await redisOps.IncrementAsync($"rate:{userId}:{window}");
        // if (count == 1)
        //     await redisOps.ExpireAsync($"rate:{userId}:{window}", TimeSpan.FromMinutes(1));
        // return count <= maxRequests;

        // Document that these operations exist and their purpose
        Assert.True(true);
    }

    /// <summary>
    /// Documents the expected usage pattern for hash operations.
    /// Common use cases: storing structured data, field-level updates.
    /// </summary>
    [Fact]
    public void HashOperations_UsagePattern_Documented()
    {
        // Hash operations are useful for:
        // 1. Storing structured data with field-level access
        // 2. Atomic field updates without full object replacement
        // 3. Efficient retrieval of partial data
        // 4. Counters per field (HINCRBY)

        // Example: Tracking per-user metrics
        // await redisOps.HashIncrementAsync($"user:{userId}:stats", "logins");
        // await redisOps.HashIncrementAsync($"user:{userId}:stats", "requests", 5);
        // var allStats = await redisOps.HashGetAllAsync($"user:{userId}:stats");

        Assert.True(true);
    }

    #endregion
}
