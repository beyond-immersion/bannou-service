// =============================================================================
// Seeded Resource Record
// Represents a static/embedded resource loaded by ISeededResourceProvider.
// =============================================================================

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Represents a seeded (embedded/static) resource.
/// </summary>
/// <remarks>
/// <para>
/// Seeded resources are read-only factory defaults provided by plugins.
/// They can represent ABML behavior definitions, scenario templates, species
/// definitions, or any other static data that plugins ship with.
/// </para>
/// <para>
/// Consumers may copy seeded resources to their own state stores for runtime
/// modification - the seeded resource itself remains immutable.
/// </para>
/// </remarks>
/// <param name="Identifier">
/// Unique identifier within the resource type.
/// For embedded resources, this is typically the filename without extension.
/// </param>
/// <param name="ResourceType">
/// Type category (e.g., "behavior", "species-definition", "scenario-template").
/// Matches the <see cref="ISeededResourceProvider.ResourceType"/> of the provider.
/// </param>
/// <param name="ContentType">
/// MIME type indicating the content format (e.g., "application/yaml", "application/json").
/// Used by consumers to determine how to parse the content.
/// </param>
/// <param name="Content">
/// Raw resource content as bytes. May be text (YAML, JSON) or binary data.
/// </param>
/// <param name="Metadata">
/// Optional key-value metadata about the resource.
/// May include version, author, description, or provider-specific attributes.
/// </param>
public sealed record SeededResource(
    string Identifier,
    string ResourceType,
    string ContentType,
    byte[] Content,
    IReadOnlyDictionary<string, string> Metadata)
{
    /// <summary>
    /// Gets the content size in bytes.
    /// </summary>
    public int SizeBytes => Content.Length;

    /// <summary>
    /// Gets the content as a UTF-8 string.
    /// </summary>
    /// <returns>The content decoded as UTF-8 text.</returns>
    /// <remarks>
    /// Use this for text-based resources (YAML, JSON, etc.).
    /// For binary resources, access <see cref="Content"/> directly.
    /// </remarks>
    public string GetContentAsString() => System.Text.Encoding.UTF8.GetString(Content);
}
