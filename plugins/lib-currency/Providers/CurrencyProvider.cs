// =============================================================================
// Currency Variable Provider
// Provides currency data for ABML expressions via ${currency.*} paths.
// Owned by lib-currency per service hierarchy (L2).
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Currency.Caching;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Currency.Providers;

/// <summary>
/// Provides currency data for ABML expressions.
/// Supports paths like ${currency.balance.GOLD}, ${currency.locked.GOLD},
/// ${currency.has_wallet}, ${currency.wallet_count}.
/// </summary>
public sealed class CurrencyProvider : IVariableProvider
{
    /// <summary>
    /// Empty provider for non-character actors.
    /// </summary>
    public static CurrencyProvider Empty { get; } = new(null);

    private readonly CachedCurrencyData? _data;

    /// <inheritdoc/>
    public string Name => VariableProviderDefinitions.Currency;

    /// <summary>
    /// Creates a new currency provider with the given cached data.
    /// </summary>
    /// <param name="data">The cached currency data, or null for empty provider.</param>
    public CurrencyProvider(CachedCurrencyData? data)
    {
        _data = data;
    }

    /// <inheritdoc/>
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();
        if (_data == null) return null;

        var firstSegment = path[0];

        // Handle ${currency.balance.{CODE}} - effective balance for a currency
        if (firstSegment.Equals("balance", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Length < 2) return null;
            var code = path[1];
            return _data.Balances.TryGetValue(code, out var balance) ? balance.EffectiveAmount : 0.0;
        }

        // Handle ${currency.locked.{CODE}} - locked amount for a currency
        if (firstSegment.Equals("locked", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Length < 2) return null;
            var code = path[1];
            return _data.Balances.TryGetValue(code, out var balance) ? balance.LockedAmount : 0.0;
        }

        // Handle ${currency.has_wallet} - whether character has any wallet
        if (firstSegment.Equals("has_wallet", StringComparison.OrdinalIgnoreCase))
        {
            return _data.WalletCount > 0;
        }

        // Handle ${currency.wallet_count} - number of active wallets
        if (firstSegment.Equals("wallet_count", StringComparison.OrdinalIgnoreCase))
        {
            return _data.WalletCount;
        }

        return null;
    }

    /// <inheritdoc/>
    public object? GetRootValue()
    {
        if (_data == null) return null;

        return new Dictionary<string, object?>
        {
            ["has_wallet"] = _data.WalletCount > 0,
            ["wallet_count"] = _data.WalletCount,
        };
    }

    /// <inheritdoc/>
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;

        var firstSegment = path[0];
        return firstSegment.Equals("balance", StringComparison.OrdinalIgnoreCase) ||
               firstSegment.Equals("locked", StringComparison.OrdinalIgnoreCase) ||
               firstSegment.Equals("has_wallet", StringComparison.OrdinalIgnoreCase) ||
               firstSegment.Equals("wallet_count", StringComparison.OrdinalIgnoreCase);
    }
}
