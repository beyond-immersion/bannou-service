using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Seed.Caching;
using BeyondImmersion.BannouService.Seed.Providers;

namespace BeyondImmersion.BannouService.Seed.Tests.Providers;

/// <summary>
/// Unit tests for SeedProvider variable resolution.
/// </summary>
public class SeedProviderTests
{
    private static readonly Guid SeedId1 = Guid.NewGuid();
    private static readonly Guid SeedId2 = Guid.NewGuid();
    private static readonly Guid GameServiceId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    [Fact]
    public void Name_ReturnsSeed()
    {
        var provider = SeedProvider.Empty;
        Assert.Equal("seed", provider.Name);
    }

    [Fact]
    public void Empty_ActiveCount_ReturnsZero()
    {
        var provider = SeedProvider.Empty;
        var result = provider.GetValue(ToSpan("active_count"));
        Assert.Equal(0, result);
    }

    [Fact]
    public void Empty_Types_ReturnsEmptyList()
    {
        var provider = SeedProvider.Empty;
        var result = provider.GetValue(ToSpan("types"));
        var types = Assert.IsType<List<string>>(result);
        Assert.Empty(types);
    }

    [Fact]
    public void Empty_UnknownType_ReturnsNull()
    {
        var provider = SeedProvider.Empty;
        var result = provider.GetValue(ToSpan("guardian"));
        Assert.Null(result);
    }

    [Fact]
    public void SingleSeed_ActiveCount_ReturnsOne()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("active_count"));
        Assert.Equal(1, result);
    }

    [Fact]
    public void SingleSeed_Types_ContainsTypeCode()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("types"));
        var types = Assert.IsType<List<string>>(result);
        Assert.Single(types);
        Assert.Contains("guardian", types);
    }

    [Fact]
    public void SingleSeed_Phase_ReturnsGrowthPhase()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("guardian", "phase"));
        Assert.Equal("seedling", result);
    }

    [Fact]
    public void SingleSeed_TotalGrowth_ReturnsValue()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("guardian", "total_growth"));
        Assert.Equal(15.5f, result);
    }

    [Fact]
    public void SingleSeed_Status_ReturnsString()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("guardian", "status"));
        Assert.Equal("Active", result);
    }

    [Fact]
    public void SingleSeed_DisplayName_ReturnsValue()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("guardian", "display_name"));
        Assert.Equal("My Guardian", result);
    }

    [Fact]
    public void SingleSeed_HasBond_ReturnsFalse()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("guardian", "has_bond"));
        Assert.Equal(false, result);
    }

    [Fact]
    public void SingleSeed_WithBond_HasBond_ReturnsTrue()
    {
        var seed = CreateSeedResponse("guardian", "seedling", 15.5f, bondId: Guid.NewGuid());
        var data = new CachedSeedData(
            new[] { seed },
            new Dictionary<Guid, GrowthResponse>(),
            new Dictionary<Guid, CapabilityManifestResponse>());
        var provider = new SeedProvider(data);

        var result = provider.GetValue(ToSpan("guardian", "has_bond"));
        Assert.Equal(true, result);
    }

    [Fact]
    public void SingleSeed_GrowthDomain_ReturnsDepth()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("guardian", "growth", "combat.melee"));
        Assert.Equal(3.2f, result);
    }

    [Fact]
    public void SingleSeed_GrowthDomain_Unknown_ReturnsNull()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("guardian", "growth", "nonexistent"));
        Assert.Null(result);
    }

    [Fact]
    public void SingleSeed_Growth_ReturnsDomainDict()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("guardian", "growth"));
        Assert.NotNull(result);
        var dict = Assert.IsAssignableFrom<IReadOnlyDictionary<string, float>>(result);
        Assert.Equal(2, dict.Count);
    }

    [Fact]
    public void SingleSeed_CapabilityFidelity_ReturnsValue()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("guardian", "capabilities", "combat_sense", "fidelity"));
        Assert.Equal(0.75f, result);
    }

    [Fact]
    public void SingleSeed_CapabilityUnlocked_ReturnsValue()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("guardian", "capabilities", "combat_sense", "unlocked"));
        Assert.Equal(true, result);
    }

    [Fact]
    public void SingleSeed_CapabilityDomain_ReturnsValue()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("guardian", "capabilities", "combat_sense", "domain"));
        Assert.Equal("combat.melee", result);
    }

    [Fact]
    public void SingleSeed_UnknownCapability_ReturnsNull()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("guardian", "capabilities", "nonexistent", "fidelity"));
        Assert.Null(result);
    }

    [Fact]
    public void SingleSeed_Capabilities_ReturnsDict()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("guardian", "capabilities"));
        Assert.NotNull(result);
    }

    [Fact]
    public void SingleSeed_UnknownProperty_ReturnsNull()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("guardian", "nonexistent"));
        Assert.Null(result);
    }

    [Fact]
    public void MultipleSeed_ActiveCount_ReturnsTwo()
    {
        var provider = CreateMultiSeedProvider();
        var result = provider.GetValue(ToSpan("active_count"));
        Assert.Equal(2, result);
    }

    [Fact]
    public void MultipleSeed_Types_ContainsBothCodes()
    {
        var provider = CreateMultiSeedProvider();
        var result = provider.GetValue(ToSpan("types"));
        var types = Assert.IsType<List<string>>(result);
        Assert.Equal(2, types.Count);
        Assert.Contains("guardian", types);
        Assert.Contains("combat_archetype", types);
    }

    [Fact]
    public void MultipleSeed_ResolvesIndependently()
    {
        var provider = CreateMultiSeedProvider();

        var guardianPhase = provider.GetValue(ToSpan("guardian", "phase"));
        var combatPhase = provider.GetValue(ToSpan("combat_archetype", "phase"));

        Assert.Equal("seedling", guardianPhase);
        Assert.Equal("awakened", combatPhase);
    }

    [Fact]
    public void TypeCode_IsCaseInsensitive()
    {
        var provider = CreateSingleSeedProvider();

        var lower = provider.GetValue(ToSpan("guardian", "phase"));
        var upper = provider.GetValue(ToSpan("Guardian", "phase"));
        var mixed = provider.GetValue(ToSpan("GUARDIAN", "phase"));

        Assert.Equal(lower, upper);
        Assert.Equal(lower, mixed);
    }

    [Fact]
    public void CanResolve_EmptyPath_ReturnsTrue()
    {
        var provider = SeedProvider.Empty;
        Assert.True(provider.CanResolve(ReadOnlySpan<string>.Empty));
    }

    [Fact]
    public void CanResolve_ActiveCount_ReturnsTrue()
    {
        var provider = SeedProvider.Empty;
        Assert.True(provider.CanResolve(ToSpan("active_count")));
    }

    [Fact]
    public void CanResolve_Types_ReturnsTrue()
    {
        var provider = SeedProvider.Empty;
        Assert.True(provider.CanResolve(ToSpan("types")));
    }

    [Fact]
    public void CanResolve_ExistingType_ReturnsTrue()
    {
        var provider = CreateSingleSeedProvider();
        Assert.True(provider.CanResolve(ToSpan("guardian")));
    }

    [Fact]
    public void CanResolve_NonExistingType_ReturnsFalse()
    {
        var provider = CreateSingleSeedProvider();
        Assert.False(provider.CanResolve(ToSpan("nonexistent")));
    }

    [Fact]
    public void GetRootValue_ReturnsActiveCountAndTypes()
    {
        var provider = CreateSingleSeedProvider();
        var root = provider.GetRootValue();
        var dict = Assert.IsType<Dictionary<string, object?>>(root);
        Assert.Equal(1, dict["active_count"]);
        var types = Assert.IsType<List<string>>(dict["types"]);
        Assert.Single(types);
    }

    [Fact]
    public void GetValue_EmptyPath_ReturnsRootValue()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ReadOnlySpan<string>.Empty);
        Assert.NotNull(result);
    }

    [Fact]
    public void GetValue_TypeCodeOnly_ReturnsSeedDict()
    {
        var provider = CreateSingleSeedProvider();
        var result = provider.GetValue(ToSpan("guardian"));
        var dict = Assert.IsType<Dictionary<string, object?>>(result);
        Assert.Equal("seedling", dict["phase"]);
        Assert.Equal(15.5f, dict["total_growth"]);
    }

    [Fact]
    public void SingleSeed_NoGrowthData_GrowthReturnsEmptyDict()
    {
        var seed = CreateSeedResponse("guardian", "initial", 0f);
        var data = new CachedSeedData(
            new[] { seed },
            new Dictionary<Guid, GrowthResponse>(),
            new Dictionary<Guid, CapabilityManifestResponse>());
        var provider = new SeedProvider(data);

        var result = provider.GetValue(ToSpan("guardian", "growth"));
        var dict = Assert.IsAssignableFrom<IReadOnlyDictionary<string, float>>(result);
        Assert.Empty(dict);
    }

    [Fact]
    public void SingleSeed_NoCapabilityData_CapabilitiesReturnsEmptyDict()
    {
        var seed = CreateSeedResponse("guardian", "initial", 0f);
        var data = new CachedSeedData(
            new[] { seed },
            new Dictionary<Guid, GrowthResponse>(),
            new Dictionary<Guid, CapabilityManifestResponse>());
        var provider = new SeedProvider(data);

        var result = provider.GetValue(ToSpan("guardian", "capabilities"));
        var dict = Assert.IsType<Dictionary<string, object?>>(result);
        Assert.Empty(dict);
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static ReadOnlySpan<string> ToSpan(params string[] segments) => segments.AsSpan();

    private static SeedResponse CreateSeedResponse(
        string typeCode, string phase, float totalGrowth,
        Guid? seedId = null, Guid? bondId = null)
    {
        return new SeedResponse
        {
            SeedId = seedId ?? SeedId1,
            OwnerId = OwnerId,
            OwnerType = EntityType.Character,
            SeedTypeCode = typeCode,
            GameServiceId = GameServiceId,
            CreatedAt = DateTimeOffset.UtcNow,
            GrowthPhase = phase,
            TotalGrowth = totalGrowth,
            BondId = bondId,
            DisplayName = "My Guardian",
            Status = SeedStatus.Active
        };
    }

    private static SeedProvider CreateSingleSeedProvider()
    {
        var seed = CreateSeedResponse("guardian", "seedling", 15.5f);

        var growth = new GrowthResponse
        {
            SeedId = SeedId1,
            TotalGrowth = 15.5f,
            Domains = new Dictionary<string, float>
            {
                ["combat.melee"] = 3.2f,
                ["exploration.discovery"] = 2.1f
            }
        };

        var manifest = new CapabilityManifestResponse
        {
            SeedId = SeedId1,
            SeedTypeCode = "guardian",
            ComputedAt = DateTimeOffset.UtcNow,
            Version = 1,
            Capabilities = new List<Capability>
            {
                new()
                {
                    CapabilityCode = "combat_sense",
                    Domain = "combat.melee",
                    Fidelity = 0.75f,
                    Unlocked = true
                },
                new()
                {
                    CapabilityCode = "pathfinding",
                    Domain = "exploration.discovery",
                    Fidelity = 0.3f,
                    Unlocked = true
                }
            }
        };

        var data = new CachedSeedData(
            new[] { seed },
            new Dictionary<Guid, GrowthResponse> { [SeedId1] = growth },
            new Dictionary<Guid, CapabilityManifestResponse> { [SeedId1] = manifest });

        return new SeedProvider(data);
    }

    private static SeedProvider CreateMultiSeedProvider()
    {
        var seed1 = CreateSeedResponse("guardian", "seedling", 15.5f, seedId: SeedId1);
        var seed2 = new SeedResponse
        {
            SeedId = SeedId2,
            OwnerId = OwnerId,
            OwnerType = EntityType.Character,
            SeedTypeCode = "combat_archetype",
            GameServiceId = GameServiceId,
            CreatedAt = DateTimeOffset.UtcNow,
            GrowthPhase = "awakened",
            TotalGrowth = 42.0f,
            DisplayName = "Combat Focus",
            Status = SeedStatus.Active
        };

        var growth1 = new GrowthResponse
        {
            SeedId = SeedId1,
            TotalGrowth = 15.5f,
            Domains = new Dictionary<string, float>
            {
                ["combat.melee"] = 3.2f
            }
        };

        var growth2 = new GrowthResponse
        {
            SeedId = SeedId2,
            TotalGrowth = 42.0f,
            Domains = new Dictionary<string, float>
            {
                ["combat.ranged"] = 10.0f
            }
        };

        var data = new CachedSeedData(
            new[] { seed1, seed2 },
            new Dictionary<Guid, GrowthResponse>
            {
                [SeedId1] = growth1,
                [SeedId2] = growth2
            },
            new Dictionary<Guid, CapabilityManifestResponse>());

        return new SeedProvider(data);
    }
}
