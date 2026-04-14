using BeyondImmersion.BannouService.Genesis;
using Xunit;

namespace BeyondImmersion.BannouService.Genesis.Tests;

/// <summary>
/// Unit tests for <see cref="GenesisGrowthState"/> — the shared state substrate between the
/// runtime pipeline components.
/// </summary>
public class GenesisGrowthStateTests
{
    [Fact]
    public void BufferGrowth_SingleEntry_AppearsInDrain()
    {
        var state = new GenesisGrowthState();
        var entityId = Guid.NewGuid();

        state.BufferGrowth(entityId, new GrowthBufferEntry("mana", 10.0, GrowthDirection.Credit));

        var drained = state.DrainAccumulator();

        Assert.Single(drained);
        Assert.True(drained.ContainsKey(entityId));
        Assert.Single(drained[entityId]);
        Assert.Equal("mana", drained[entityId][0].WalletCode);
        Assert.Equal(10.0, drained[entityId][0].Amount);
        Assert.Equal(GrowthDirection.Credit, drained[entityId][0].Direction);
    }

    [Fact]
    public void BufferGrowth_MultipleEntriesForSameEntity_AllAppearInDrain()
    {
        var state = new GenesisGrowthState();
        var entityId = Guid.NewGuid();

        state.BufferGrowth(entityId, new GrowthBufferEntry("mana", 10.0, GrowthDirection.Credit));
        state.BufferGrowth(entityId, new GrowthBufferEntry("mana", 20.0, GrowthDirection.Credit));
        state.BufferGrowth(entityId, new GrowthBufferEntry("experience", 5.0, GrowthDirection.Credit));

        var drained = state.DrainAccumulator();

        Assert.Single(drained);
        Assert.Equal(3, drained[entityId].Count);
        Assert.Equal(30.0, drained[entityId].Where(e => e.WalletCode == "mana").Sum(e => e.Amount));
        Assert.Equal(5.0, drained[entityId].First(e => e.WalletCode == "experience").Amount);
    }

    [Fact]
    public void BufferGrowth_EntriesForDifferentEntities_GroupedByEntityId()
    {
        var state = new GenesisGrowthState();
        var entityA = Guid.NewGuid();
        var entityB = Guid.NewGuid();

        state.BufferGrowth(entityA, new GrowthBufferEntry("mana", 10.0, GrowthDirection.Credit));
        state.BufferGrowth(entityB, new GrowthBufferEntry("mana", 20.0, GrowthDirection.Credit));
        state.BufferGrowth(entityA, new GrowthBufferEntry("mana", 15.0, GrowthDirection.Credit));

        var drained = state.DrainAccumulator();

        Assert.Equal(2, drained.Count);
        Assert.Equal(2, drained[entityA].Count);
        Assert.Single(drained[entityB]);
        Assert.Equal(25.0, drained[entityA].Sum(e => e.Amount));
        Assert.Equal(20.0, drained[entityB].Sum(e => e.Amount));
    }

    [Fact]
    public void DrainAccumulator_Empty_ReturnsEmptyDictionary()
    {
        var state = new GenesisGrowthState();

        var drained = state.DrainAccumulator();

        Assert.Empty(drained);
    }

    [Fact]
    public void DrainAccumulator_AfterDrain_SubsequentDrainIsEmpty()
    {
        var state = new GenesisGrowthState();
        var entityId = Guid.NewGuid();

        state.BufferGrowth(entityId, new GrowthBufferEntry("mana", 10.0, GrowthDirection.Credit));

        var firstDrain = state.DrainAccumulator();
        var secondDrain = state.DrainAccumulator();

        Assert.Single(firstDrain);
        Assert.Empty(secondDrain);
    }

    [Fact]
    public void BufferGrowth_AfterDrain_AppearsInNextDrain()
    {
        var state = new GenesisGrowthState();
        var entityId = Guid.NewGuid();

        state.BufferGrowth(entityId, new GrowthBufferEntry("mana", 10.0, GrowthDirection.Credit));
        state.DrainAccumulator();

        state.BufferGrowth(entityId, new GrowthBufferEntry("mana", 20.0, GrowthDirection.Credit));
        var drained = state.DrainAccumulator();

        Assert.Single(drained);
        Assert.Single(drained[entityId]);
        Assert.Equal(20.0, drained[entityId][0].Amount);
    }

    [Fact]
    public void TryGetWalletMapping_FreshState_ReturnsFalse()
    {
        var state = new GenesisGrowthState();
        Assert.False(state.TryGetWalletMapping(Guid.NewGuid(), out _));
    }

    [Fact]
    public void SetWalletMapping_StoresAndAllowsLookup()
    {
        var state = new GenesisGrowthState();
        var walletId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var mapping = new GenesisWalletMapping(
            EntityId: entityId,
            TemplateCode: "treasure_chest",
            WalletCode: "mana",
            GrowthMappings: new List<GenesisGrowthMapping>());

        state.SetWalletMapping(walletId, mapping);

        Assert.True(state.TryGetWalletMapping(walletId, out var result));
        Assert.Equal(entityId, result.EntityId);
        Assert.Equal("treasure_chest", result.TemplateCode);
        Assert.Equal("mana", result.WalletCode);
    }

    [Fact]
    public void TryRemoveWalletMapping_RemovesEntry()
    {
        var state = new GenesisGrowthState();
        var walletId = Guid.NewGuid();
        state.SetWalletMapping(walletId, new GenesisWalletMapping(
            EntityId: Guid.NewGuid(),
            TemplateCode: "treasure_chest",
            WalletCode: "mana",
            GrowthMappings: new List<GenesisGrowthMapping>()));

        Assert.True(state.TryRemoveWalletMapping(walletId));
        Assert.False(state.TryGetWalletMapping(walletId, out _));
        // Idempotent — second remove returns false
        Assert.False(state.TryRemoveWalletMapping(walletId));
    }

    [Fact]
    public void TryGetActorTemplate_FreshState_ReturnsFalse()
    {
        var state = new GenesisGrowthState();
        Assert.False(state.TryGetActorTemplate("any-key", out _));
        Assert.False(state.ContainsActorTemplate("any-key"));
    }

    [Fact]
    public void SetActorTemplate_StoresAndAllowsLookup()
    {
        var state = new GenesisGrowthState();
        var templateId = Guid.NewGuid();
        var key = GenesisSeedEvolutionListener.BuildActorTemplateKey("treasure_chest", "Stirring");

        state.SetActorTemplate(key, templateId);

        Assert.True(state.ContainsActorTemplate(key));
        Assert.True(state.TryGetActorTemplate(key, out var actual));
        Assert.Equal(templateId, actual);
    }

    [Fact]
    public void BufferGrowth_ConcurrentWrites_AllPreserved()
    {
        var state = new GenesisGrowthState();
        var entityId = Guid.NewGuid();
        const int writerCount = 10;
        const int entriesPerWriter = 100;

        Parallel.For(0, writerCount, writerId =>
        {
            for (var i = 0; i < entriesPerWriter; i++)
                state.BufferGrowth(entityId, new GrowthBufferEntry("mana", 1.0, GrowthDirection.Credit));
        });

        var drained = state.DrainAccumulator();
        Assert.Single(drained);
        Assert.Equal(writerCount * entriesPerWriter, drained[entityId].Count);
        Assert.Equal(writerCount * entriesPerWriter, (int)drained[entityId].Sum(e => e.Amount));
    }
}
