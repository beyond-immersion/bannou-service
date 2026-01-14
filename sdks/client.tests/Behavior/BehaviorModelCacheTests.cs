// =============================================================================
// Behavior Model Cache Tests
// Tests for interpreter caching and variant fallback chains.
// =============================================================================

using BeyondImmersion.Bannou.Client.Behavior;
using BeyondImmersion.Bannou.Client.Behavior.Intent;
using BeyondImmersion.Bannou.Client.Behavior.Runtime;
using Xunit;

namespace BeyondImmersion.Bannou.Client.Tests.Behavior;

/// <summary>
/// Tests for the BehaviorModelCache class.
/// Verifies model registration, interpreter caching, and variant fallback.
/// </summary>
public class BehaviorModelCacheTests
{
    // =========================================================================
    // BASIC REGISTRATION TESTS
    // =========================================================================

    [Fact]
    public void RegisterModel_AddsToCache()
    {
        var cache = new BehaviorModelCache();
        var model = CreateTestModel();

        cache.RegisterModel(BehaviorModelType.Combat, "default", model);

        Assert.Equal(1, cache.ModelCount);
        Assert.NotNull(cache.GetModel(BehaviorModelType.Combat, "default"));
    }

    [Fact]
    public void RegisterModel_NullVariant_UsesDefault()
    {
        var cache = new BehaviorModelCache();
        var model = CreateTestModel();

        cache.RegisterModel(BehaviorModelType.Combat, null!, model);

        Assert.NotNull(cache.GetModel(BehaviorModelType.Combat, BehaviorModelCache.DefaultVariant));
    }

    [Fact]
    public void UnregisterModel_RemovesFromCache()
    {
        var cache = new BehaviorModelCache();
        var model = CreateTestModel();
        cache.RegisterModel(BehaviorModelType.Combat, "default", model);

        var removed = cache.UnregisterModel(BehaviorModelType.Combat, "default");

        Assert.True(removed);
        Assert.Equal(0, cache.ModelCount);
        Assert.Null(cache.GetModel(BehaviorModelType.Combat, "default"));
    }

    [Fact]
    public void UnregisterModel_NotFound_ReturnsFalse()
    {
        var cache = new BehaviorModelCache();

        var removed = cache.UnregisterModel(BehaviorModelType.Combat, "nonexistent");

        Assert.False(removed);
    }

    // =========================================================================
    // INTERPRETER CACHING TESTS
    // =========================================================================

    [Fact]
    public void GetInterpreter_CreatesInterpreterForModel()
    {
        var cache = new BehaviorModelCache();
        var model = CreateTestModel();
        cache.RegisterModel(BehaviorModelType.Combat, "default", model);
        var characterId = Guid.NewGuid();

        var interpreter = cache.GetInterpreter(characterId, BehaviorModelType.Combat, "default");

        Assert.NotNull(interpreter);
        Assert.Equal(1, cache.CacheCount);
    }

    [Fact]
    public void GetInterpreter_ReturnsSameInstance()
    {
        var cache = new BehaviorModelCache();
        var model = CreateTestModel();
        cache.RegisterModel(BehaviorModelType.Combat, "default", model);
        var characterId = Guid.NewGuid();

        var interpreter1 = cache.GetInterpreter(characterId, BehaviorModelType.Combat, "default");
        var interpreter2 = cache.GetInterpreter(characterId, BehaviorModelType.Combat, "default");

        Assert.Same(interpreter1, interpreter2);
        Assert.Equal(1, cache.CacheCount);
    }

    [Fact]
    public void GetInterpreter_DifferentCharacters_DifferentInstances()
    {
        var cache = new BehaviorModelCache();
        var model = CreateTestModel();
        cache.RegisterModel(BehaviorModelType.Combat, "default", model);
        var character1 = Guid.NewGuid();
        var character2 = Guid.NewGuid();

        var interpreter1 = cache.GetInterpreter(character1, BehaviorModelType.Combat, "default");
        var interpreter2 = cache.GetInterpreter(character2, BehaviorModelType.Combat, "default");

        Assert.NotSame(interpreter1, interpreter2);
        Assert.Equal(2, cache.CacheCount);
    }

    [Fact]
    public void GetInterpreter_NoModelRegistered_ReturnsNull()
    {
        var cache = new BehaviorModelCache();
        var characterId = Guid.NewGuid();

        var interpreter = cache.GetInterpreter(characterId, BehaviorModelType.Combat, "default");

        Assert.Null(interpreter);
        Assert.Equal(0, cache.CacheCount);
    }

    // =========================================================================
    // FALLBACK CHAIN TESTS
    // =========================================================================

    [Fact]
    public void GetInterpreter_FallsBackToChain()
    {
        var cache = new BehaviorModelCache();
        var defaultModel = CreateTestModel();
        cache.RegisterModel(BehaviorModelType.Combat, "default", defaultModel);
        cache.SetFallbackChain(BehaviorModelType.Combat, new[] { "sword-and-shield", "one-handed", "default" });
        var characterId = Guid.NewGuid();

        // Request specific variant that's not registered
        var interpreter = cache.GetInterpreter(characterId, BehaviorModelType.Combat, "sword-and-shield");

        // Should fall back to "default"
        Assert.NotNull(interpreter);
    }

    [Fact]
    public void GetInterpreter_UsesPreferredVariantIfAvailable()
    {
        var cache = new BehaviorModelCache();
        var defaultModel = CreateTestModel();
        var swordModel = CreateTestModel();
        cache.RegisterModel(BehaviorModelType.Combat, "default", defaultModel);
        cache.RegisterModel(BehaviorModelType.Combat, "sword-and-shield", swordModel);
        cache.SetFallbackChain(BehaviorModelType.Combat, new[] { "sword-and-shield", "default" });
        var characterId = Guid.NewGuid();

        var interpreter = cache.GetInterpreter(characterId, BehaviorModelType.Combat, "sword-and-shield");

        // Should use preferred variant, not fallback
        Assert.NotNull(interpreter);
        Assert.Equal(swordModel.Id, interpreter.ModelId);
    }

    [Fact]
    public void GetFallbackChain_DefaultIsDefault()
    {
        var cache = new BehaviorModelCache();

        var chain = cache.GetFallbackChain(BehaviorModelType.Combat);

        Assert.Single(chain);
        Assert.Equal("default", chain[0]);
    }

    [Fact]
    public void SetFallbackChain_OverridesDefault()
    {
        var cache = new BehaviorModelCache();
        var newChain = new[] { "variant1", "variant2", "default" };

        cache.SetFallbackChain(BehaviorModelType.Combat, newChain);

        var chain = cache.GetFallbackChain(BehaviorModelType.Combat);
        Assert.Equal(3, chain.Length);
        Assert.Equal("variant1", chain[0]);
        Assert.Equal("variant2", chain[1]);
        Assert.Equal("default", chain[2]);
    }

    // =========================================================================
    // INVALIDATION TESTS
    // =========================================================================

    [Fact]
    public void Invalidate_RemovesCharacterTypeFromCache()
    {
        var cache = new BehaviorModelCache();
        var model = CreateTestModel();
        cache.RegisterModel(BehaviorModelType.Combat, "default", model);
        var characterId = Guid.NewGuid();
        cache.GetInterpreter(characterId, BehaviorModelType.Combat, "default");
        Assert.Equal(1, cache.CacheCount);

        cache.Invalidate(characterId, BehaviorModelType.Combat);

        Assert.Equal(0, cache.CacheCount);
    }

    [Fact]
    public void Invalidate_LeavesOtherCharactersIntact()
    {
        var cache = new BehaviorModelCache();
        var model = CreateTestModel();
        cache.RegisterModel(BehaviorModelType.Combat, "default", model);
        var character1 = Guid.NewGuid();
        var character2 = Guid.NewGuid();
        cache.GetInterpreter(character1, BehaviorModelType.Combat, "default");
        cache.GetInterpreter(character2, BehaviorModelType.Combat, "default");
        Assert.Equal(2, cache.CacheCount);

        cache.Invalidate(character1, BehaviorModelType.Combat);

        Assert.Equal(1, cache.CacheCount);
    }

    [Fact]
    public void InvalidateAll_RemovesAllForCharacter()
    {
        var cache = new BehaviorModelCache();
        var combatModel = CreateTestModel();
        var movementModel = CreateTestModel();
        cache.RegisterModel(BehaviorModelType.Combat, "default", combatModel);
        cache.RegisterModel(BehaviorModelType.Movement, "default", movementModel);
        var characterId = Guid.NewGuid();
        cache.GetInterpreter(characterId, BehaviorModelType.Combat, "default");
        cache.GetInterpreter(characterId, BehaviorModelType.Movement, "default");
        Assert.Equal(2, cache.CacheCount);

        cache.InvalidateAll(characterId);

        Assert.Equal(0, cache.CacheCount);
    }

    [Fact]
    public void ClearCache_RemovesAll()
    {
        var cache = new BehaviorModelCache();
        var model = CreateTestModel();
        cache.RegisterModel(BehaviorModelType.Combat, "default", model);
        var character1 = Guid.NewGuid();
        var character2 = Guid.NewGuid();
        cache.GetInterpreter(character1, BehaviorModelType.Combat, "default");
        cache.GetInterpreter(character2, BehaviorModelType.Combat, "default");
        Assert.Equal(2, cache.CacheCount);

        cache.ClearCache();

        Assert.Equal(0, cache.CacheCount);
        // Models should still be registered
        Assert.Equal(1, cache.ModelCount);
    }

    // =========================================================================
    // MULTIPLE MODEL TYPES TESTS
    // =========================================================================

    [Fact]
    public void MultipleModelTypes_IndependentCaching()
    {
        var cache = new BehaviorModelCache();
        var combatModel = CreateTestModel();
        var movementModel = CreateTestModel();
        cache.RegisterModel(BehaviorModelType.Combat, "default", combatModel);
        cache.RegisterModel(BehaviorModelType.Movement, "default", movementModel);
        var characterId = Guid.NewGuid();

        var combatInterpreter = cache.GetInterpreter(characterId, BehaviorModelType.Combat, "default");
        var movementInterpreter = cache.GetInterpreter(characterId, BehaviorModelType.Movement, "default");

        Assert.NotNull(combatInterpreter);
        Assert.NotNull(movementInterpreter);
        Assert.NotSame(combatInterpreter, movementInterpreter);
        Assert.Equal(combatModel.Id, combatInterpreter.ModelId);
        Assert.Equal(movementModel.Id, movementInterpreter.ModelId);
    }

    [Fact]
    public void DifferentVariantsSameType_IndependentModels()
    {
        var cache = new BehaviorModelCache();
        var swordModel = CreateTestModel();
        var axeModel = CreateTestModel();
        cache.RegisterModel(BehaviorModelType.Combat, "sword", swordModel);
        cache.RegisterModel(BehaviorModelType.Combat, "axe", axeModel);
        var characterId = Guid.NewGuid();

        var swordInterpreter = cache.GetInterpreter(characterId, BehaviorModelType.Combat, "sword");
        var axeInterpreter = cache.GetInterpreter(characterId, BehaviorModelType.Combat, "axe");

        Assert.NotNull(swordInterpreter);
        Assert.NotNull(axeInterpreter);
        Assert.NotEqual(swordInterpreter.ModelId, axeInterpreter.ModelId);
    }

    // =========================================================================
    // HELPER METHODS
    // =========================================================================

    private static BehaviorModel CreateTestModel()
    {
        // Create a minimal valid behavior model for testing
        var header = new BehaviorModelHeader(
            version: BehaviorModelHeader.CurrentVersion,
            flags: BehaviorModelFlags.None,
            modelId: Guid.NewGuid(),
            checksum: 0);

        var schema = new StateSchema(
            inputs: Array.Empty<VariableDefinition>(),
            outputs: Array.Empty<VariableDefinition>());

        return new BehaviorModel(
            header: header,
            extensionHeader: null,
            schema: schema,
            continuationPoints: ContinuationPointTable.Empty,
            constantPool: Array.Empty<double>(),
            stringTable: Array.Empty<string>(),
            bytecode: new byte[] { (byte)BehaviorOpcode.Halt });
    }
}
