// =============================================================================
// Relationship Variable Provider Factory
// Creates RelationshipProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Relationship.Caching;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Relationship.Providers;

/// <summary>
/// Factory for creating RelationshipProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
[BannouHelperService("relationship-provider", typeof(IRelationshipService), typeof(IVariableProviderFactory), lifetime: ServiceLifetime.Singleton)]
public sealed class RelationshipProviderFactory : IVariableProviderFactory
{
    private readonly IRelationshipDataCache _cache;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new relationship provider factory.
    /// </summary>
    public RelationshipProviderFactory(IRelationshipDataCache cache, ITelemetryProvider telemetryProvider)
    {
        _cache = cache;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.Relationship;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipProviderFactory.CreateAsync");
        if (!characterId.HasValue)
        {
            return RelationshipProvider.Empty;
        }

        var data = await _cache.GetOrLoadAsync(characterId.Value, ct);
        return new RelationshipProvider(data ?? CachedRelationshipData.Empty);
    }
}
