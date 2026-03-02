using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Worldstate.Providers;

/// <summary>
/// Factory for creating WorldProvider instances that expose <c>${world.*}</c>
/// variables for ABML expression evaluation.
/// Registered with DI as <see cref="IVariableProviderFactory"/> for Actor to discover.
/// </summary>
/// <remarks>
/// <para>
/// Worldstate is realm-scoped: the <c>characterId</c> and <c>locationId</c> parameters
/// are ignored. The <c>realmId</c> parameter determines which realm's game clock
/// and calendar to expose.
/// </para>
/// <para>
/// If no clock is initialized for the realm, returns <see cref="WorldProvider.Empty"/>
/// which returns null/default for all variables.
/// </para>
/// <para>
/// Resolves internal cache services via <see cref="IServiceProvider"/> to avoid
/// exposing internal types in the public constructor (CS0051 prevention).
/// </para>
/// </remarks>
public sealed class WorldProviderFactory : IVariableProviderFactory
{
    private readonly IRealmClockCache _clockCache;
    private readonly ICalendarTemplateCache _calendarCache;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new WorldProviderFactory.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving internal cache services.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public WorldProviderFactory(
        IServiceProvider serviceProvider,
        ITelemetryProvider telemetryProvider)
    {
        _clockCache = serviceProvider.GetRequiredService<IRealmClockCache>();
        _calendarCache = serviceProvider.GetRequiredService<ICalendarTemplateCache>();
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.World;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.worldstate", "WorldProviderFactory.CreateAsync");

        // Load realm clock from cache (backed by Redis)
        var clock = await _clockCache.GetOrLoadAsync(realmId, ct);
        if (clock == null)
        {
            // No clock initialized for this realm — return empty provider
            return WorldProvider.Empty;
        }

        // Load calendar template from cache (backed by MySQL) for derived variables
        var calendar = await _calendarCache.GetOrLoadAsync(
            clock.GameServiceId, clock.CalendarTemplateCode, ct);

        return new WorldProvider(clock, calendar);
    }
}
