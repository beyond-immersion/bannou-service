// =============================================================================
// Embedded Resource Provider Base Class
// Base implementation for providers that load from assembly embedded resources.
// =============================================================================

using System.Reflection;

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Base class for seeded resource providers that load from assembly embedded resources.
/// </summary>
/// <remarks>
/// <para>
/// Subclasses specify the assembly, resource prefix, and resource type.
/// Resources are discovered by scanning the assembly's embedded resources
/// that match the specified prefix.
/// </para>
/// <para>
/// <b>Naming Convention</b>: Embedded resources should follow the pattern
/// <c>{Namespace}.{Folder}.{filename.ext}</c> where the identifier is derived
/// from the filename portion (with or without extension, depending on
/// <see cref="IncludeExtensionInIdentifier"/>).
/// </para>
/// <para>
/// <b>Example Usage</b>:
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
/// <para>
/// <b>Registration</b>: Register in DI during plugin startup:
/// </para>
/// <code>
/// services.AddSingleton&lt;ISeededResourceProvider, BehaviorSeededProvider&gt;();
/// </code>
/// </remarks>
public abstract class EmbeddedResourceProvider : ISeededResourceProvider
{
    private List<string>? _cachedIdentifiers;
    private readonly object _cacheLock = new();

    /// <inheritdoc />
    public abstract string ResourceType { get; }

    /// <summary>
    /// Gets the MIME content type for resources from this provider.
    /// </summary>
    /// <remarks>
    /// Common values:
    /// <list type="bullet">
    ///   <item>"application/yaml" - YAML files</item>
    ///   <item>"application/json" - JSON files</item>
    ///   <item>"text/plain" - Plain text files</item>
    ///   <item>"application/octet-stream" - Binary files</item>
    /// </list>
    /// </remarks>
    public abstract string ContentType { get; }

    /// <summary>
    /// Gets the assembly containing the embedded resources.
    /// </summary>
    protected abstract Assembly ResourceAssembly { get; }

    /// <summary>
    /// Gets the resource name prefix used to filter embedded resources.
    /// </summary>
    /// <remarks>
    /// Should end with a period (e.g., "BeyondImmersion.LibActor.Behaviors.").
    /// Only resources starting with this prefix will be discovered.
    /// </remarks>
    protected abstract string ResourcePrefix { get; }

    /// <summary>
    /// Gets whether to include the file extension in the resource identifier.
    /// </summary>
    /// <remarks>
    /// When false (default), "MyBehavior.yaml" becomes identifier "MyBehavior".
    /// When true, "MyBehavior.yaml" becomes identifier "MyBehavior.yaml".
    /// </remarks>
    protected virtual bool IncludeExtensionInIdentifier => false;

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListSeededAsync(CancellationToken ct)
    {
        var identifiers = GetCachedIdentifiers();
        return Task.FromResult<IReadOnlyList<string>>(identifiers);
    }

    /// <inheritdoc />
    public Task<SeededResource?> GetSeededAsync(string identifier, CancellationToken ct)
    {
        var resourceName = GetResourceNameForIdentifier(identifier);
        if (resourceName == null)
        {
            return Task.FromResult<SeededResource?>(null);
        }

        using var stream = ResourceAssembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return Task.FromResult<SeededResource?>(null);
        }

        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        var content = memoryStream.ToArray();

        var metadata = GetMetadata(identifier, resourceName);

        var resource = new SeededResource(
            Identifier: identifier,
            ResourceType: ResourceType,
            ContentType: ContentType,
            Content: content,
            Metadata: metadata);

        return Task.FromResult<SeededResource?>(resource);
    }

    /// <summary>
    /// Gets metadata for a resource. Override to provide custom metadata.
    /// </summary>
    /// <param name="identifier">The resource identifier.</param>
    /// <param name="resourceName">The full embedded resource name.</param>
    /// <returns>Metadata dictionary. Default implementation returns empty dictionary.</returns>
    protected virtual IReadOnlyDictionary<string, string> GetMetadata(string identifier, string resourceName)
    {
        return new Dictionary<string, string>();
    }

    /// <summary>
    /// Derives the identifier from an embedded resource name.
    /// </summary>
    /// <param name="resourceName">The full embedded resource name (e.g., "Namespace.Folder.File.yaml").</param>
    /// <returns>The identifier (e.g., "File" or "File.yaml" depending on settings).</returns>
    protected virtual string DeriveIdentifier(string resourceName)
    {
        // Remove the prefix to get the filename portion
        var filename = resourceName[ResourcePrefix.Length..];

        if (IncludeExtensionInIdentifier)
        {
            return filename;
        }

        // Remove extension
        var lastDot = filename.LastIndexOf('.');
        return lastDot > 0 ? filename[..lastDot] : filename;
    }

    private List<string> GetCachedIdentifiers()
    {
        if (_cachedIdentifiers != null)
        {
            return _cachedIdentifiers;
        }

        lock (_cacheLock)
        {
            if (_cachedIdentifiers != null)
            {
                return _cachedIdentifiers;
            }

            var resourceNames = ResourceAssembly.GetManifestResourceNames();
            var identifiers = resourceNames
                .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                .Select(DeriveIdentifier)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _cachedIdentifiers = identifiers;
            return _cachedIdentifiers;
        }
    }

    private string? GetResourceNameForIdentifier(string identifier)
    {
        var resourceNames = ResourceAssembly.GetManifestResourceNames();

        foreach (var resourceName in resourceNames)
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var derivedIdentifier = DeriveIdentifier(resourceName);
            if (string.Equals(derivedIdentifier, identifier, StringComparison.OrdinalIgnoreCase))
            {
                return resourceName;
            }
        }

        return null;
    }
}
