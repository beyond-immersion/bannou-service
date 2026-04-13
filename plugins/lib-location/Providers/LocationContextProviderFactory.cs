// =============================================================================
// Location Context Variable Provider Factory
// Creates LocationContextProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Location.Caching;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Location.Providers;

/// <summary>
/// Factory for creating LocationContextProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
[BannouHelperService("location-context-provider", typeof(ILocationService), typeof(IVariableProviderFactory), lifetime: ServiceLifetime.Singleton)]
public sealed class LocationContextProviderFactory : IVariableProviderFactory
{
    private readonly ILocationDataCache _cache;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new location context provider factory.
    /// </summary>
    /// <param name="cache">The location data cache for loading context data.</param>
    /// <param name="telemetryProvider">Telemetry provider for distributed tracing spans.</param>
    public LocationContextProviderFactory(ILocationDataCache cache, ITelemetryProvider telemetryProvider)
    {
        _cache = cache;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.Location;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.location", "LocationContextProviderFactory.Create");

        if (!characterId.HasValue)
        {
            return LocationContextProvider.Empty;
        }

        var context = await _cache.GetOrLoadLocationContextAsync(characterId.Value, realmId, locationId, ct);
        return new LocationContextProvider(context);
    }
}
