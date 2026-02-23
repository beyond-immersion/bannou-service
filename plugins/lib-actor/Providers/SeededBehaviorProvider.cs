// =============================================================================
// Seeded Behavior Provider
// Loads pre-defined ABML behaviors from embedded assembly resources.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.Bannou.BehaviorCompiler.Parser;
using BeyondImmersion.BannouService.Providers;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace BeyondImmersion.BannouService.Actor.Providers;

/// <summary>
/// Behavior provider that loads pre-defined ABML behaviors from embedded assembly resources.
/// </summary>
/// <remarks>
/// <para>
/// Seeded behaviors are YAML files embedded in the lib-actor assembly under the
/// <c>Behaviors/</c> folder. They represent the "factory defaults" for common
/// actor behavior patterns (humanoid base, creature base, etc.).
/// </para>
/// <para>
/// Standard priority levels:
/// <list type="bullet">
///   <item>100 - DynamicBehaviorProvider (lib-puppetmaster): Asset-based behaviors</item>
///   <item>50 - SeededBehaviorProvider (lib-actor): Embedded behaviors</item>
///   <item>0 - FallbackBehaviorProvider (lib-actor): Graceful degradation</item>
/// </list>
/// </para>
/// <para>
/// <b>Embedded Resource Naming</b>: Resources must follow the pattern
/// <c>BeyondImmersion.BannouService.Actor.Behaviors.{name}.yaml</c>
/// where <c>{name}</c> is the identifier used to load the behavior.
/// </para>
/// </remarks>
public sealed class SeededBehaviorProvider : IBehaviorDocumentProvider
{
    private const string ResourcePrefix = "BeyondImmersion.BannouService.Actor.Behaviors.";
    private const string SeededPrefix = "seeded:";

    private readonly ILogger<SeededBehaviorProvider> _logger;
    private readonly DocumentParser _parser = new();
    private readonly ConcurrentDictionary<string, AbmlDocument> _cache = new();
    private readonly Lazy<IReadOnlyList<string>> _availableIdentifiers;

    /// <summary>
    /// Creates a new seeded behavior provider.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public SeededBehaviorProvider(ILogger<SeededBehaviorProvider> logger)
    {
        _logger = logger;
        _availableIdentifiers = new Lazy<IReadOnlyList<string>>(DiscoverEmbeddedBehaviors);

        // Log available seeded behaviors on construction (for debugging)
        var count = _availableIdentifiers.Value.Count;
        if (count > 0)
        {
            _logger.LogInformation(
                "SeededBehaviorProvider initialized with {Count} behaviors: {Behaviors}",
                count,
                string.Join(", ", _availableIdentifiers.Value));
        }
        else
        {
            _logger.LogDebug("SeededBehaviorProvider initialized with no embedded behaviors");
        }
    }

    /// <inheritdoc />
    /// <remarks>Priority 50 = medium, checked after dynamic providers but before fallback.</remarks>
    public int Priority => 50;

    /// <summary>
    /// Gets the list of available seeded behavior identifiers.
    /// </summary>
    public IReadOnlyList<string> AvailableIdentifiers => _availableIdentifiers.Value;

    /// <inheritdoc />
    /// <remarks>
    /// Returns true if the behavior reference matches a seeded behavior pattern:
    /// <list type="bullet">
    ///   <item><c>seeded:{name}</c> - Explicit seeded reference</item>
    ///   <item><c>{name}</c> - Direct identifier match (when in available list)</item>
    /// </list>
    /// </remarks>
    public bool CanProvide(string behaviorRef)
    {
        if (string.IsNullOrWhiteSpace(behaviorRef))
        {
            return false;
        }

        var identifier = ExtractIdentifier(behaviorRef);
        return _availableIdentifiers.Value.Contains(identifier, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<AbmlDocument?> GetDocumentAsync(string behaviorRef, CancellationToken ct)
    {
        var identifier = ExtractIdentifier(behaviorRef);

        // Check cache first
        if (_cache.TryGetValue(identifier, out var cached))
        {
            _logger.LogDebug("Seeded behavior cache hit: {Identifier}", identifier);
            return cached;
        }

        // Load from embedded resource
        var resourceName = $"{ResourcePrefix}{identifier}.yaml";
        var assembly = typeof(SeededBehaviorProvider).Assembly;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            _logger.LogDebug("No embedded resource found for seeded behavior {Identifier}", identifier);
            return null;
        }

        // Read and parse YAML
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var yaml = await reader.ReadToEndAsync(ct);

        var result = _parser.Parse(yaml);
        if (!result.IsSuccess || result.Value == null)
        {
            _logger.LogError(
                "Failed to parse seeded behavior {Identifier}: {Errors}",
                identifier,
                string.Join(", ", result.Errors.Select(e => e.Message)));
            return null;
        }

        // Cache the parsed document
        _cache.TryAdd(identifier, result.Value);
        _logger.LogDebug("Loaded seeded behavior {Identifier}", identifier);

        return result.Value;
    }

    /// <inheritdoc />
    public void Invalidate(string behaviorRef)
    {
        var identifier = ExtractIdentifier(behaviorRef);
        if (_cache.TryRemove(identifier, out _))
        {
            _logger.LogDebug("Invalidated seeded behavior cache for {Identifier}", identifier);
        }
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        var count = _cache.Count;
        _cache.Clear();
        if (count > 0)
        {
            _logger.LogDebug("Invalidated {Count} cached seeded behaviors", count);
        }
    }

    /// <summary>
    /// Extracts the behavior identifier from a reference.
    /// </summary>
    /// <param name="behaviorRef">The behavior reference.</param>
    /// <returns>The identifier (e.g., "humanoid_base" from "seeded:humanoid_base").</returns>
    private static string ExtractIdentifier(string behaviorRef)
    {
        if (behaviorRef.StartsWith(SeededPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return behaviorRef[SeededPrefix.Length..];
        }
        return behaviorRef;
    }

    /// <summary>
    /// Discovers all embedded behavior YAML files in the assembly.
    /// </summary>
    /// <returns>List of available behavior identifiers.</returns>
    private IReadOnlyList<string> DiscoverEmbeddedBehaviors()
    {
        var assembly = typeof(SeededBehaviorProvider).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();

        var identifiers = resourceNames
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) &&
                            name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            .Select(name =>
            {
                // Extract identifier: remove prefix and .yaml suffix
                var withoutPrefix = name[ResourcePrefix.Length..];
                var identifier = withoutPrefix[..^5]; // Remove .yaml
                return identifier;
            })
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return identifiers;
    }
}
