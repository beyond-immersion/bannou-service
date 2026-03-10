// =============================================================================
// Currency Variable Provider Factory
// Creates CurrencyProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Currency.Caching;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using System.Diagnostics;
using BeyondImmersion.BannouService.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Currency.Providers;

/// <summary>
/// Factory for creating CurrencyProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
[BannouHelperService("currency-provider", typeof(ICurrencyService), typeof(IVariableProviderFactory), lifetime: ServiceLifetime.Singleton)]
public sealed class CurrencyProviderFactory : IVariableProviderFactory
{
    private readonly ICurrencyDataCache _cache;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new currency provider factory.
    /// </summary>
    public CurrencyProviderFactory(ICurrencyDataCache cache, ITelemetryProvider telemetryProvider)
    {
        _cache = cache;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.Currency;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyProviderFactory.CreateAsync");
        if (!characterId.HasValue)
        {
            return CurrencyProvider.Empty;
        }

        var data = await _cache.GetOrLoadAsync(characterId.Value, realmId, ct);
        return new CurrencyProvider(data);
    }
}
