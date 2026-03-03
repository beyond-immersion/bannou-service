// =============================================================================
// Cached Currency Data Model
// Stores aggregated currency balance data for variable provider consumption.
// =============================================================================

namespace BeyondImmersion.BannouService.Currency.Caching;

/// <summary>
/// Aggregated currency balance data for a character, cached for ABML variable provider access.
/// Balances are keyed by currency code (case-insensitive) and include both realm-scoped and global wallets.
/// </summary>
public sealed record CachedCurrencyData
{
    /// <summary>
    /// Empty instance for non-character actors or when no wallet data exists.
    /// </summary>
    public static CachedCurrencyData Empty { get; } = new();

    /// <summary>
    /// Currency balances keyed by currency code (case-insensitive).
    /// Each balance aggregates across realm-scoped and global wallets.
    /// </summary>
    public IReadOnlyDictionary<string, CurrencyBalance> Balances { get; init; }
        = new Dictionary<string, CurrencyBalance>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Number of active wallets for this character.
    /// </summary>
    public int WalletCount { get; init; }
}

/// <summary>
/// Balance data for a single currency code, aggregated across wallets.
/// </summary>
/// <param name="Amount">Total available amount.</param>
/// <param name="LockedAmount">Total locked (held) amount.</param>
/// <param name="EffectiveAmount">Total effective amount (amount minus locked).</param>
public sealed record CurrencyBalance(double Amount, double LockedAmount, double EffectiveAmount);
