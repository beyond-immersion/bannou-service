using BeyondImmersion.BannouService.Relationship.Caching;
using BeyondImmersion.BannouService.Relationship.Providers;

namespace BeyondImmersion.BannouService.Relationship.Tests.Providers;

/// <summary>
/// Unit tests for RelationshipProvider variable resolution.
/// </summary>
public class RelationshipProviderTests
{
    [Fact]
    public void Name_ReturnsRelationship()
    {
        var provider = RelationshipProvider.Empty;
        Assert.Equal("relationship", provider.Name);
    }

    [Fact]
    public void Empty_Has_ReturnsFalse()
    {
        var provider = RelationshipProvider.Empty;
        var result = provider.GetValue(ToSpan("has", "PARENT"));
        Assert.Equal(false, result);
    }

    [Fact]
    public void Empty_Count_ReturnsZero()
    {
        var provider = RelationshipProvider.Empty;
        var result = provider.GetValue(ToSpan("count", "PARENT"));
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Empty_Total_ReturnsZero()
    {
        var provider = RelationshipProvider.Empty;
        var result = provider.GetValue(ToSpan("total"));
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void WithData_Has_ExistingType_ReturnsTrue()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("has", "PARENT"));
        Assert.Equal(true, result);
    }

    [Fact]
    public void WithData_Has_MissingType_ReturnsFalse()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("has", "ENEMY"));
        Assert.Equal(false, result);
    }

    [Fact]
    public void WithData_Count_ReturnsCount()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("count", "ALLY"));
        Assert.Equal(3.0, result);
    }

    [Fact]
    public void WithData_Count_MissingType_ReturnsZero()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("count", "ENEMY"));
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void WithData_Total_ReturnsTotalCount()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("total"));
        Assert.Equal(6.0, result);
    }

    [Fact]
    public void WithData_Has_MissingSubpath_ReturnsNull()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("has"));
        Assert.Null(result);
    }

    [Fact]
    public void WithData_Count_MissingSubpath_ReturnsNull()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("count"));
        Assert.Null(result);
    }

    [Fact]
    public void WithData_UnknownPath_ReturnsNull()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("nonexistent"));
        Assert.Null(result);
    }

    [Fact]
    public void PathResolution_IsCaseInsensitive()
    {
        var provider = CreateProvider();

        var lower = provider.GetValue(ToSpan("has", "parent"));
        var upper = provider.GetValue(ToSpan("HAS", "PARENT"));
        var mixed = provider.GetValue(ToSpan("Has", "Parent"));

        Assert.Equal(lower, upper);
        Assert.Equal(lower, mixed);
    }

    [Fact]
    public void CanResolve_EmptyPath_ReturnsTrue()
    {
        var provider = RelationshipProvider.Empty;
        Assert.True(provider.CanResolve(ReadOnlySpan<string>.Empty));
    }

    [Fact]
    public void CanResolve_ValidPaths_ReturnsTrue()
    {
        var provider = RelationshipProvider.Empty;
        Assert.True(provider.CanResolve(ToSpan("has")));
        Assert.True(provider.CanResolve(ToSpan("count")));
        Assert.True(provider.CanResolve(ToSpan("total")));
    }

    [Fact]
    public void CanResolve_InvalidPath_ReturnsFalse()
    {
        var provider = RelationshipProvider.Empty;
        Assert.False(provider.CanResolve(ToSpan("nonexistent")));
        Assert.False(provider.CanResolve(ToSpan("type")));
    }

    [Fact]
    public void GetRootValue_ReturnsTotalCount()
    {
        var provider = CreateProvider();
        var root = provider.GetRootValue();
        var dict = Assert.IsType<Dictionary<string, object?>>(root);
        Assert.Equal(6.0, dict["total"]);
    }

    [Fact]
    public void GetValue_EmptyPath_ReturnsRootValue()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ReadOnlySpan<string>.Empty);
        Assert.NotNull(result);
        var dict = Assert.IsType<Dictionary<string, object?>>(result);
        Assert.Equal(6.0, dict["total"]);
    }

    [Fact]
    public void MultipleTypes_ResolvesIndependently()
    {
        var provider = CreateProvider();

        var parentCount = provider.GetValue(ToSpan("count", "PARENT"));
        var allyCount = provider.GetValue(ToSpan("count", "ALLY"));
        var siblingCount = provider.GetValue(ToSpan("count", "SIBLING"));

        Assert.Equal(2.0, parentCount);
        Assert.Equal(3.0, allyCount);
        Assert.Equal(1.0, siblingCount);
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static ReadOnlySpan<string> ToSpan(params string[] segments) => segments.AsSpan();

    private static RelationshipProvider CreateProvider()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["PARENT"] = 2,
            ["ALLY"] = 3,
            ["SIBLING"] = 1,
        };

        var data = new CachedRelationshipData
        {
            CountsByTypeCode = counts,
            TotalCount = 6,
        };

        return new RelationshipProvider(data);
    }
}
