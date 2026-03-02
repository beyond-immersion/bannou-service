// =============================================================================
// Transit Cost Modifier Provider Interface
// Enables dependency inversion for Transit's cost enrichment pattern.
// Higher-layer services (L4) implement this to enrich transit costs
// without creating hierarchy violations.
// =============================================================================

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Provides cost modifiers for transit calculations.
/// Higher-layer services (L4) implement this to enrich transit costs
/// without creating hierarchy violations.
/// </summary>
/// <remarks>
/// <para>
/// This interface enables the dependency inversion pattern for Transit cost enrichment:
/// </para>
/// <list type="bullet">
///   <item>Transit (L2) defines this interface and accepts providers via DI collection</item>
///   <item>Higher-layer services (L4) implement providers and register them</item>
///   <item>Transit queries all registered providers to enrich cost calculations</item>
/// </list>
/// <para>
/// <b>DISTRIBUTED SAFETY</b>: This is a pull-based provider pattern.
/// The consumer (Transit, L2) initiates the request on whichever node
/// needs the data. The provider runs co-located and reads from distributed
/// state (Redis/MySQL). Always safe in multi-node deployments per
/// SERVICE-HIERARCHY.md DI Provider pattern.
/// </para>
/// <para>
/// <b>Aggregation Rules</b>:
/// </para>
/// <list type="bullet">
///   <item><see cref="TransitCostModifier.PreferenceCostDelta"/>: summed across providers (clamped to [MinPreferenceCost, MaxPreferenceCost] from config, defaults [0.0, 2.0])</item>
///   <item><see cref="TransitCostModifier.SpeedMultiplier"/>: multiplied across providers (clamped to [MinSpeedMultiplier, MaxSpeedMultiplier] from config, defaults [0.1, 3.0])</item>
///   <item><see cref="TransitCostModifier.RiskDelta"/>: summed across providers (clamped to [0.0, 1.0] total risk)</item>
///   <item>Graceful degradation: if a provider throws, Transit logs a warning and skips it</item>
/// </list>
/// <para>
/// <b>Expected Implementations</b>:
/// </para>
/// <list type="bullet">
///   <item>"proficiency": Speed bonus from seed-derived travel proficiency via Status</item>
///   <item>"disposition": Preference cost from feelings toward transit modes</item>
///   <item>"hearsay": Risk perception from believed dangers along connections</item>
///   <item>"weather": Speed reduction from weather conditions on connections</item>
///   <item>"faction": Risk from hostile territory</item>
///   <item>"ethology": Species-level mode affinity</item>
/// </list>
/// <para>
/// <b>Example Implementation</b>:
/// </para>
/// <code>
/// public class DispositionCostModifierProvider : ITransitCostModifierProvider
/// {
///     public string ProviderName => "disposition";
///
///     public async Task&lt;TransitCostModifier&gt; GetModifierAsync(
///         Guid entityId, string entityType, string modeCode,
///         Guid? connectionId, CancellationToken ct)
///     {
///         var dread = await _dispositionClient.GetFeelingAsync(entityId, modeCode, ct);
///         return new TransitCostModifier(
///             PreferenceCostDelta: dread,
///             SpeedMultiplier: 1.0m,
///             RiskDelta: 0.0m,
///             Reason: $"Dread of {modeCode}: {dread}");
///     }
/// }
/// // DI registration: services.AddSingleton&lt;ITransitCostModifierProvider, DispositionCostModifierProvider&gt;();
/// </code>
/// </remarks>
public interface ITransitCostModifierProvider
{
    /// <summary>
    /// Gets the provider name for logging and debugging (e.g., "weather", "disposition").
    /// </summary>
    /// <remarks>
    /// Used for diagnostic logging when providers are queried. Multiple providers
    /// can coexist; each contributes independently to the final cost modifier.
    /// </remarks>
    string ProviderName { get; }

    /// <summary>
    /// Gets the cost modifier for a specific entity, mode, and connection.
    /// Returns a modifier that affects GOAP preference cost, travel speed, and risk assessment.
    /// </summary>
    /// <param name="entityId">The entity requesting cost data (e.g., character ID).</param>
    /// <param name="entityType">The entity type discriminator (e.g., "character", "caravan").</param>
    /// <param name="modeCode">The transit mode code being evaluated (e.g., "horseback").</param>
    /// <param name="connectionId">The specific connection being evaluated, or null when evaluating mode availability without a specific connection context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A cost modifier. Implementations should return neutral values
    /// (0.0 deltas, 1.0 multiplier) when they have no data for this entity/mode/connection
    /// combination, rather than throwing.
    /// </returns>
    Task<TransitCostModifier> GetModifierAsync(
        Guid entityId,
        string entityType,
        string modeCode,
        Guid? connectionId,
        CancellationToken ct);
}

/// <summary>
/// A cost modifier returned by an <see cref="ITransitCostModifierProvider"/>.
/// Transit aggregates modifiers from all registered providers to compute
/// effective travel costs for GOAP decision-making.
/// </summary>
/// <param name="PreferenceCostDelta">
/// Additive preference cost (0.0 = no effect, positive = aversion, negative = affinity).
/// Aggregated by summing across providers, then clamped to [MinPreferenceCost, MaxPreferenceCost] from config (defaults [0.0, 2.0]).
/// </param>
/// <param name="SpeedMultiplier">
/// Speed multiplier (1.0 = no effect, 0.5 = half speed, 1.5 = 50% faster).
/// Aggregated by multiplying across providers, then clamped to [MinSpeedMultiplier, MaxSpeedMultiplier] from config (defaults [0.1, 3.0]).
/// </param>
/// <param name="RiskDelta">
/// Risk additive (0.0 = no effect, positive = more dangerous).
/// Aggregated by summing across providers. Total risk (base + deltas) clamped to [0.0, 1.0].
/// </param>
/// <param name="Reason">
/// Human-readable reason for this modifier. Used for debugging and logging.
/// </param>
public record TransitCostModifier(
    decimal PreferenceCostDelta,
    decimal SpeedMultiplier,
    decimal RiskDelta,
    string Reason);
