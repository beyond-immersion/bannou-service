using BeyondImmersion.BannouService.Currency.Caching;
using BeyondImmersion.BannouService.Currency.Providers;

namespace BeyondImmersion.BannouService.Currency.Tests.Providers;

/// <summary>
/// Unit tests for CurrencyProvider variable resolution.
/// </summary>
public class CurrencyProviderTests
{
    [Fact]
    public void Name_ReturnsCurrency()
    {
        var provider = CurrencyProvider.Empty;
        Assert.Equal("currency", provider.Name);
    }

    [Fact]
    public void Empty_HasWallet_ReturnsNull()
    {
        var provider = CurrencyProvider.Empty;
        var result = provider.GetValue(ToSpan("has_wallet"));
        Assert.Null(result);
    }

    [Fact]
    public void Empty_WalletCount_ReturnsNull()
    {
        var provider = CurrencyProvider.Empty;
        var result = provider.GetValue(ToSpan("wallet_count"));
        Assert.Null(result);
    }

    [Fact]
    public void Empty_Balance_ReturnsNull()
    {
        var provider = CurrencyProvider.Empty;
        var result = provider.GetValue(ToSpan("balance", "GOLD"));
        Assert.Null(result);
    }

    [Fact]
    public void Empty_Locked_ReturnsNull()
    {
        var provider = CurrencyProvider.Empty;
        var result = provider.GetValue(ToSpan("locked", "GOLD"));
        Assert.Null(result);
    }

    [Fact]
    public void Empty_GetRootValue_ReturnsNull()
    {
        var provider = CurrencyProvider.Empty;
        Assert.Null(provider.GetRootValue());
    }

    [Fact]
    public void WithData_HasWallet_ReturnsTrue()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("has_wallet"));
        Assert.Equal(true, result);
    }

    [Fact]
    public void WithData_WalletCount_ReturnsCount()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("wallet_count"));
        Assert.Equal(2, result);
    }

    [Fact]
    public void WithData_Balance_ReturnsEffectiveAmount()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("balance", "GOLD"));
        Assert.Equal(85.0, result);
    }

    [Fact]
    public void WithData_Locked_ReturnsLockedAmount()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("locked", "GOLD"));
        Assert.Equal(15.0, result);
    }

    [Fact]
    public void WithData_Balance_MissingCode_ReturnsZero()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("balance", "DIAMOND"));
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void WithData_Balance_MissingSubpath_ReturnsNull()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("balance"));
        Assert.Null(result);
    }

    [Fact]
    public void WithData_Locked_MissingSubpath_ReturnsNull()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ToSpan("locked"));
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
    public void WithData_MultipleCurrencies()
    {
        var provider = CreateProvider();
        var gold = provider.GetValue(ToSpan("balance", "GOLD"));
        var silver = provider.GetValue(ToSpan("balance", "SILVER"));
        Assert.Equal(85.0, gold);
        Assert.Equal(250.0, silver);
    }

    [Fact]
    public void PathResolution_IsCaseInsensitive()
    {
        var provider = CreateProvider();

        var lower = provider.GetValue(ToSpan("balance", "gold"));
        var upper = provider.GetValue(ToSpan("BALANCE", "GOLD"));
        var mixed = provider.GetValue(ToSpan("Balance", "Gold"));

        Assert.Equal(lower, upper);
        Assert.Equal(lower, mixed);
    }

    [Fact]
    public void CanResolve_EmptyPath_ReturnsTrue()
    {
        var provider = CurrencyProvider.Empty;
        Assert.True(provider.CanResolve(ReadOnlySpan<string>.Empty));
    }

    [Fact]
    public void CanResolve_ValidPaths_ReturnsTrue()
    {
        var provider = CurrencyProvider.Empty;
        Assert.True(provider.CanResolve(ToSpan("balance")));
        Assert.True(provider.CanResolve(ToSpan("locked")));
        Assert.True(provider.CanResolve(ToSpan("has_wallet")));
        Assert.True(provider.CanResolve(ToSpan("wallet_count")));
    }

    [Fact]
    public void CanResolve_InvalidPath_ReturnsFalse()
    {
        var provider = CurrencyProvider.Empty;
        Assert.False(provider.CanResolve(ToSpan("nonexistent")));
        Assert.False(provider.CanResolve(ToSpan("amount")));
    }

    [Fact]
    public void GetRootValue_ReturnsHasWalletAndWalletCount()
    {
        var provider = CreateProvider();
        var root = provider.GetRootValue();
        var dict = Assert.IsType<Dictionary<string, object?>>(root);
        Assert.Equal(true, dict["has_wallet"]);
        Assert.Equal(2, dict["wallet_count"]);
    }

    [Fact]
    public void GetValue_EmptyPath_ReturnsRootValue()
    {
        var provider = CreateProvider();
        var result = provider.GetValue(ReadOnlySpan<string>.Empty);
        Assert.NotNull(result);
        var dict = Assert.IsType<Dictionary<string, object?>>(result);
        Assert.Equal(true, dict["has_wallet"]);
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static ReadOnlySpan<string> ToSpan(params string[] segments) => segments.AsSpan();

    private static CurrencyProvider CreateProvider()
    {
        var balances = new Dictionary<string, CurrencyBalance>(StringComparer.OrdinalIgnoreCase)
        {
            ["GOLD"] = new CurrencyBalance(100.0, 15.0, 85.0),
            ["SILVER"] = new CurrencyBalance(250.0, 0.0, 250.0),
        };

        var data = new CachedCurrencyData
        {
            Balances = balances,
            WalletCount = 2,
        };

        return new CurrencyProvider(data);
    }
}
