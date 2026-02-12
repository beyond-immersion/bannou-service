using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Location.Providers;

namespace BeyondImmersion.BannouService.Location.Tests.Providers;

/// <summary>
/// Unit tests for LocationContextProvider variable resolution.
/// </summary>
public class LocationContextProviderTests
{
    [Fact]
    public void Name_ReturnsLocation()
    {
        var provider = LocationContextProvider.Empty;
        Assert.Equal("location", provider.Name);
    }

    [Fact]
    public void Empty_GetValue_Zone_ReturnsNull()
    {
        var provider = LocationContextProvider.Empty;
        var result = provider.GetValue(ToSpan("zone"));
        Assert.Null(result);
    }

    [Fact]
    public void Empty_GetRootValue_ReturnsNull()
    {
        var provider = LocationContextProvider.Empty;
        Assert.Null(provider.GetRootValue());
    }

    [Fact]
    public void Empty_GetValue_EmptyPath_ReturnsNull()
    {
        var provider = LocationContextProvider.Empty;
        var result = provider.GetValue(ReadOnlySpan<string>.Empty);
        Assert.Null(result);
    }

    [Fact]
    public void WithData_Zone_ReturnsCode()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("zone"));
        Assert.Equal("MARKET_DISTRICT", result);
    }

    [Fact]
    public void WithData_Name_ReturnsName()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("name"));
        Assert.Equal("Market District", result);
    }

    [Fact]
    public void WithData_Region_ReturnsRegionCode()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("region"));
        Assert.Equal("CENTRAL_REGION", result);
    }

    [Fact]
    public void WithData_Region_NoRegion_ReturnsNull()
    {
        var provider = CreateProvider(region: null);
        var result = provider.GetValue(ToSpan("region"));
        Assert.Null(result);
    }

    [Fact]
    public void WithData_Type_ReturnsLocationTypeString()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("type"));
        Assert.Equal("DISTRICT", result);
    }

    [Fact]
    public void WithData_Depth_ReturnsDepth()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("depth"));
        Assert.Equal(3, result);
    }

    [Fact]
    public void WithData_Realm_ReturnsRealmCode()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("realm"));
        Assert.Equal("ARCADIA", result);
    }

    [Fact]
    public void WithData_NearbyPois_ReturnsList()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("nearby_pois"));
        var pois = Assert.IsType<List<string>>(result);
        Assert.Equal(2, pois.Count);
        Assert.Contains("TEMPLE_DISTRICT", pois);
        Assert.Contains("HARBOR", pois);
    }

    [Fact]
    public void WithData_EntityCount_ReturnsCount()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("entity_count"));
        Assert.Equal(42, result);
    }

    [Fact]
    public void WithData_UnknownPath_ReturnsNull()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("nonexistent"));
        Assert.Null(result);
    }

    [Fact]
    public void CanResolve_EmptyPath_ReturnsTrue()
    {
        var provider = LocationContextProvider.Empty;
        Assert.True(provider.CanResolve(ReadOnlySpan<string>.Empty));
    }

    [Fact]
    public void CanResolve_ValidPaths_ReturnsTrue()
    {
        var provider = LocationContextProvider.Empty;
        Assert.True(provider.CanResolve(ToSpan("zone")));
        Assert.True(provider.CanResolve(ToSpan("name")));
        Assert.True(provider.CanResolve(ToSpan("region")));
        Assert.True(provider.CanResolve(ToSpan("type")));
        Assert.True(provider.CanResolve(ToSpan("depth")));
        Assert.True(provider.CanResolve(ToSpan("realm")));
        Assert.True(provider.CanResolve(ToSpan("nearby_pois")));
        Assert.True(provider.CanResolve(ToSpan("entity_count")));
    }

    [Fact]
    public void CanResolve_InvalidPath_ReturnsFalse()
    {
        var provider = LocationContextProvider.Empty;
        Assert.False(provider.CanResolve(ToSpan("nonexistent")));
        Assert.False(provider.CanResolve(ToSpan("is_safe")));
    }

    [Fact]
    public void GetRootValue_ReturnsAllFields()
    {
        var provider = CreateProvider();
        var root = provider.GetRootValue();
        var dict = Assert.IsType<Dictionary<string, object?>>(root);

        Assert.Equal("MARKET_DISTRICT", dict["zone"]);
        Assert.Equal("Market District", dict["name"]);
        Assert.Equal("CENTRAL_REGION", dict["region"]);
        Assert.Equal("DISTRICT", dict["type"]);
        Assert.Equal(3, dict["depth"]);
        Assert.Equal("ARCADIA", dict["realm"]);
        var pois = Assert.IsType<List<string>>(dict["nearby_pois"]);
        Assert.Equal(2, pois.Count);
        Assert.Equal(42, dict["entity_count"]);
    }

    [Fact]
    public void GetValue_EmptyPath_ReturnsRootValue()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ReadOnlySpan<string>.Empty);
        Assert.NotNull(result);
        var dict = Assert.IsType<Dictionary<string, object?>>(result);
        Assert.Equal("MARKET_DISTRICT", dict["zone"]);
    }

    [Fact]
    public void PathResolution_IsCaseInsensitive()
    {
        var provider = CreateProvider();

        var lower = provider.GetValue(ToSpan("zone"));
        var upper = provider.GetValue(ToSpan("ZONE"));
        var mixed = provider.GetValue(ToSpan("Zone"));

        Assert.Equal(lower, upper);
        Assert.Equal(lower, mixed);
    }

    [Fact]
    public void CanResolve_IsCaseInsensitive()
    {
        var provider = LocationContextProvider.Empty;

        Assert.True(provider.CanResolve(ToSpan("entity_count")));
        Assert.True(provider.CanResolve(ToSpan("ENTITY_COUNT")));
        Assert.True(provider.CanResolve(ToSpan("Entity_Count")));
    }

    [Fact]
    public void WithData_EmptyNearbyPois_ReturnsEmptyList()
    {
        var data = new LocationContextData(
            Zone: "ISOLATED_CAVE",
            Name: "Isolated Cave",
            Region: null,
            Type: LocationType.DUNGEON,
            Depth: 5,
            Realm: "ARCADIA",
            NearbyPois: new List<string>(),
            EntityCount: 0);

        var provider = new LocationContextProvider(data);
        var result = provider.GetValue(ToSpan("nearby_pois"));
        var pois = Assert.IsType<List<string>>(result);
        Assert.Empty(pois);
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static ReadOnlySpan<string> ToSpan(params string[] segments) => segments.AsSpan();

    private static LocationContextProvider CreateProvider(string? region = "CENTRAL_REGION")
    {
        var data = new LocationContextData(
            Zone: "MARKET_DISTRICT",
            Name: "Market District",
            Region: region,
            Type: LocationType.DISTRICT,
            Depth: 3,
            Realm: "ARCADIA",
            NearbyPois: new List<string> { "TEMPLE_DISTRICT", "HARBOR" },
            EntityCount: 42);

        return new LocationContextProvider(data);
    }
}
