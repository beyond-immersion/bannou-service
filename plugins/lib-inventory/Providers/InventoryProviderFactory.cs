// =============================================================================
// Inventory Variable Provider Factory
// Creates InventoryProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Inventory.Caching;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Inventory.Providers;

/// <summary>
/// Factory for creating InventoryProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
public sealed class InventoryProviderFactory : IVariableProviderFactory
{
    private readonly IInventoryDataCache _cache;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new inventory provider factory.
    /// </summary>
    public InventoryProviderFactory(IInventoryDataCache cache, ITelemetryProvider telemetryProvider)
    {
        _cache = cache;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.Inventory;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryProviderFactory.CreateAsync");
        if (!characterId.HasValue)
        {
            return InventoryProvider.Empty;
        }

        var data = await _cache.GetOrLoadAsync(characterId.Value, ct);
        return new InventoryProvider(data ?? CachedInventoryData.Empty);
    }
}
