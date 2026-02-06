// =============================================================================
// Seeded Resource Provider Interface
// Enables dependency inversion for static/embedded resource loading.
// Higher-layer services implement this to provide seeded resources to lib-resource (L1).
// =============================================================================

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Factory interface for providing seeded (embedded/static) resources.
/// </summary>
/// <remarks>
/// <para>
/// This interface enables the dependency inversion pattern for seeded resource loading:
/// </para>
/// <list type="bullet">
///   <item>lib-resource (L1) defines this interface and discovers providers via DI</item>
///   <item>Higher-layer services (L2/L3/L4) implement providers and register them</item>
///   <item>ResourceService queries all registered providers to list/get seeded resources</item>
/// </list>
/// <para>
/// <b>Implementation Guidelines</b>:
/// </para>
/// <list type="bullet">
///   <item>Providers should load from embedded resources, config directories, or env-specified paths</item>
///   <item>Return null from <see cref="GetSeededAsync"/> when resource not found (not an exception)</item>
///   <item>The <see cref="ResourceType"/> identifies the category (e.g., "behavior", "species-definition")</item>
///   <item>Use <see cref="EmbeddedResourceProvider"/> as a base class for assembly-embedded resources</item>
/// </list>
/// <para>
/// <b>Example Implementation</b>:
/// </para>
/// <code>
/// public class BehaviorSeededProvider : EmbeddedResourceProvider
/// {
///     public override string ResourceType => "behavior";
///     public override string ContentType => "application/yaml";
///     protected override Assembly ResourceAssembly => typeof(BehaviorSeededProvider).Assembly;
///     protected override string ResourcePrefix => "BeyondImmersion.LibActor.Behaviors.";
/// }
/// </code>
/// </remarks>
public interface ISeededResourceProvider
{
    /// <summary>
    /// Gets the resource type this provider handles (e.g., "behavior", "scenario-template").
    /// </summary>
    /// <remarks>
    /// This identifies the category of resources. Multiple providers can register
    /// for the same resource type, and their resources will be merged in list operations.
    /// </remarks>
    string ResourceType { get; }

    /// <summary>
    /// Returns all seeded resource identifiers available from this provider.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// List of resource identifiers. Each identifier must be unique within this provider's
    /// <see cref="ResourceType"/>. Empty list if no resources available.
    /// </returns>
    Task<IReadOnlyList<string>> ListSeededAsync(CancellationToken ct);

    /// <summary>
    /// Loads a seeded resource by identifier.
    /// </summary>
    /// <param name="identifier">The resource identifier (as returned by <see cref="ListSeededAsync"/>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The seeded resource with content and metadata, or null if not found.
    /// Implementations should return null rather than throwing for missing resources.
    /// </returns>
    Task<SeededResource?> GetSeededAsync(string identifier, CancellationToken ct);
}
