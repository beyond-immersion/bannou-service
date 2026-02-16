// =============================================================================
// Variable Provider Factory Interface
// Enables dependency inversion for Actor's data access pattern.
// Higher-layer services implement this to register data providers with Actor (L2).
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Factory interface for creating variable providers for ABML expression evaluation.
/// </summary>
/// <remarks>
/// <para>
/// This interface enables the dependency inversion pattern for Actor data access:
/// </para>
/// <list type="bullet">
///   <item>Actor (L2) defines this interface and accepts factories via DI</item>
///   <item>Higher-layer services (L3/L4) implement factories and register them</item>
///   <item>ActorRunner queries all registered factories to build execution scope</item>
/// </list>
/// <para>
/// <b>Implementation Guidelines</b>:
/// </para>
/// <list type="bullet">
///   <item>Factories should handle caching internally if needed</item>
///   <item>Return empty/default providers when entity has no data (not null)</item>
///   <item>The <see cref="ProviderName"/> must match the <see cref="IVariableProvider.Name"/></item>
/// </list>
/// <para>
/// <b>Example Implementation</b>:
/// </para>
/// <code>
/// public class PersonalityProviderFactory : IVariableProviderFactory
/// {
///     public string ProviderName => "personality";
///
///     public async Task&lt;IVariableProvider&gt; CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
///     {
///         if (!characterId.HasValue) return PersonalityProvider.Empty;
///         var data = await _cache.GetOrLoadAsync(characterId.Value, ct);
///         return new PersonalityProvider(data);
///     }
/// }
/// </code>
/// </remarks>
public interface IVariableProviderFactory
{
    /// <summary>
    /// Gets the name of providers this factory creates (e.g., "personality", "quest").
    /// </summary>
    /// <remarks>
    /// This must match the <see cref="IVariableProvider.Name"/> of created providers.
    /// Used for debugging and to prevent duplicate registrations.
    /// </remarks>
    string ProviderName { get; }

    /// <summary>
    /// Creates a variable provider for the given entity.
    /// </summary>
    /// <param name="characterId">
    /// The character ID to create a provider for, or null for non-character actors.
    /// Null for scheduled-task or world-admin actors that aren't bound to characters.
    /// When non-null, this is always a character ID (including divine characters in system realms).
    /// </param>
    /// <param name="realmId">
    /// The realm the actor operates in. Always provided — every actor belongs to a realm.
    /// Used by realm-scoped providers (e.g., Worldstate for game clock, Faction for realm filtering).
    /// </param>
    /// <param name="locationId">
    /// The actor's current location, or null if not yet known.
    /// Tracked by ActorRunner from incoming perception events. May be null on first
    /// execution scope creation before any perception events arrive.
    /// Used by location-aware providers (e.g., Location to skip entity-location lookup).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A provider instance. Implementations should return an empty/default provider
    /// rather than null when the entity has no data for this provider type.
    /// </returns>
    Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct);
}
