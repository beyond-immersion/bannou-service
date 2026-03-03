using BeyondImmersion.BannouService.Inventory.Caching;
using BeyondImmersion.BannouService.Inventory.Providers;

namespace BeyondImmersion.BannouService.Inventory.Tests.Providers;

/// <summary>
/// Unit tests for InventoryProvider variable resolution.
/// </summary>
public class InventoryProviderTests
{
    [Fact]
    public void Name_ReturnsInventory()
    {
        var provider = InventoryProvider.Empty;
        Assert.Equal("inventory", provider.Name);
    }

    [Fact]
    public void Empty_HasItem_ReturnsNull()
    {
        var provider = InventoryProvider.Empty;
        var result = provider.GetValue(ToSpan("has_item", "IRON_SWORD"));
        Assert.Equal(false, result);
    }

    [Fact]
    public void Empty_Count_ReturnsZero()
    {
        var provider = InventoryProvider.Empty;
        var result = provider.GetValue(ToSpan("count", "IRON_SWORD"));
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Empty_HasSpace_ReturnsFalse()
    {
        var provider = InventoryProvider.Empty;
        var result = provider.GetValue(ToSpan("has_space"));
        Assert.Equal(false, result);
    }

    [Fact]
    public void Empty_TotalContainers_ReturnsZero()
    {
        var provider = InventoryProvider.Empty;
        var result = provider.GetValue(ToSpan("total_containers"));
        Assert.Equal(0, result);
    }

    [Fact]
    public void Empty_TotalItems_ReturnsZero()
    {
        var provider = InventoryProvider.Empty;
        var result = provider.GetValue(ToSpan("total_items"));
        Assert.Equal(0, result);
    }

    [Fact]
    public void Empty_TotalWeight_ReturnsZero()
    {
        var provider = InventoryProvider.Empty;
        var result = provider.GetValue(ToSpan("total_weight"));
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Empty_UsedSlots_ReturnsZero()
    {
        var provider = InventoryProvider.Empty;
        var result = provider.GetValue(ToSpan("used_slots"));
        Assert.Equal(0, result);
    }

    [Fact]
    public void WithData_HasItem_ExistingItem_ReturnsTrue()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("has_item", "IRON_SWORD"));
        Assert.Equal(true, result);
    }

    [Fact]
    public void WithData_HasItem_MissingItem_ReturnsFalse()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("has_item", "DIAMOND"));
        Assert.Equal(false, result);
    }

    [Fact]
    public void WithData_Count_ReturnsQuantity()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("count", "IRON_ORE"));
        Assert.Equal(25.0, result);
    }

    [Fact]
    public void WithData_Count_MissingItem_ReturnsZero()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("count", "DIAMOND"));
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void WithData_HasSpace_ReturnsTrue()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("has_space"));
        Assert.Equal(true, result);
    }

    [Fact]
    public void WithData_TotalContainers_ReturnsCount()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("total_containers"));
        Assert.Equal(3, result);
    }

    [Fact]
    public void WithData_TotalItems_ReturnsCount()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("total_items"));
        Assert.Equal(28, result);
    }

    [Fact]
    public void WithData_TotalWeight_ReturnsWeight()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("total_weight"));
        Assert.Equal(42.5, result);
    }

    [Fact]
    public void WithData_UsedSlots_ReturnsCount()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("used_slots"));
        Assert.Equal(12, result);
    }

    [Fact]
    public void WithData_UnknownPath_ReturnsNull()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("nonexistent"));
        Assert.Null(result);
    }

    [Fact]
    public void WithData_HasItem_MissingSubpath_ReturnsNull()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("has_item"));
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
    public void PathResolution_IsCaseInsensitive()
    {
        var provider = CreateProvider();

        var lower = provider.GetValue(ToSpan("has_item", "iron_sword"));
        var upper = provider.GetValue(ToSpan("HAS_ITEM", "IRON_SWORD"));
        var mixed = provider.GetValue(ToSpan("Has_Item", "Iron_Sword"));

        Assert.Equal(lower, upper);
        Assert.Equal(lower, mixed);
    }

    [Fact]
    public void CanResolve_EmptyPath_ReturnsTrue()
    {
        var provider = InventoryProvider.Empty;
        Assert.True(provider.CanResolve(ReadOnlySpan<string>.Empty));
    }

    [Fact]
    public void CanResolve_ValidPaths_ReturnsTrue()
    {
        var provider = InventoryProvider.Empty;
        Assert.True(provider.CanResolve(ToSpan("has_item")));
        Assert.True(provider.CanResolve(ToSpan("count")));
        Assert.True(provider.CanResolve(ToSpan("has_space")));
        Assert.True(provider.CanResolve(ToSpan("total_containers")));
        Assert.True(provider.CanResolve(ToSpan("total_items")));
        Assert.True(provider.CanResolve(ToSpan("total_weight")));
        Assert.True(provider.CanResolve(ToSpan("used_slots")));
    }

    [Fact]
    public void CanResolve_InvalidPath_ReturnsFalse()
    {
        var provider = InventoryProvider.Empty;
        Assert.False(provider.CanResolve(ToSpan("nonexistent")));
        Assert.False(provider.CanResolve(ToSpan("capacity")));
    }

    [Fact]
    public void GetRootValue_ReturnsAggregateStats()
    {
        var provider = CreateProvider();
        var root = provider.GetRootValue();
        var dict = Assert.IsType<Dictionary<string, object?>>(root);
        Assert.Equal(true, dict["has_space"]);
        Assert.Equal(3, dict["total_containers"]);
        Assert.Equal(28, dict["total_items"]);
        Assert.Equal(42.5, dict["total_weight"]);
        Assert.Equal(12, dict["used_slots"]);
    }

    [Fact]
    public void GetValue_EmptyPath_ReturnsRootValue()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ReadOnlySpan<string>.Empty);
        Assert.NotNull(result);
        var dict = Assert.IsType<Dictionary<string, object?>>(result);
        Assert.Equal(true, dict["has_space"]);
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static ReadOnlySpan<string> ToSpan(params string[] segments) => segments.AsSpan();

    private static InventoryProvider CreateProvider()
    {
        var itemCounts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["IRON_SWORD"] = 1.0,
            ["IRON_ORE"] = 25.0,
            ["HEALTH_POTION"] = 2.0,
        };

        var data = new CachedInventoryData
        {
            ItemCountsByTemplateCode = itemCounts,
            TotalContainers = 3,
            TotalItemCount = 28,
            TotalWeight = 42.5,
            UsedSlots = 12,
            HasSpace = true,
        };

        return new InventoryProvider(data);
    }
}
