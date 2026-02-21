// =============================================================================
// Resource Template Registry
// Singleton registry for managing resource templates.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Templates;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.ResourceTemplates;

/// <summary>
/// Singleton registry of resource templates.
/// Plugins register their templates during OnRunningAsync.
/// </summary>
/// <remarks>
/// <para>
/// This registry maintains two concurrent lookup indexes:
/// <list type="bullet">
///   <item>By sourceType: for validating ABML resource_templates metadata</item>
///   <item>By namespace: for resolving expression paths</item>
/// </list>
/// </para>
/// <para>
/// Registration is thread-safe using <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Both indexes are updated atomically - if namespace registration fails,
/// the sourceType registration is rolled back.
/// </para>
/// <para>
/// <b>Plugin Registration Pattern</b>:
/// </para>
/// <code>
/// public class CharacterPersonalityServicePlugin : BannouServicePlugin
/// {
///     private readonly IResourceTemplateRegistry _templateRegistry;
///
///     public CharacterPersonalityServicePlugin(
///         IResourceTemplateRegistry templateRegistry, ...)
///     {
///         _templateRegistry = templateRegistry;
///     }
///
///     protected override Task OnRunningAsync(CancellationToken ct)
///     {
///         _templateRegistry.Register(new CharacterPersonalityTemplate());
///         return Task.CompletedTask;
///     }
/// }
/// </code>
/// </remarks>
public sealed class ResourceTemplateRegistry : IResourceTemplateRegistry
{
    private readonly ConcurrentDictionary<string, IResourceTemplate> _bySourceType =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, IResourceTemplate> _byNamespace =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<ResourceTemplateRegistry> _logger;

    /// <summary>
    /// Creates a new resource template registry.
    /// </summary>
    /// <param name="logger">Logger for registration events.</param>
    public ResourceTemplateRegistry(ILogger<ResourceTemplateRegistry> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IResourceTemplate? GetBySourceType(string sourceType)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        return _bySourceType.TryGetValue(sourceType, out var template) ? template : null;
    }

    /// <inheritdoc />
    public IResourceTemplate? GetByNamespace(string @namespace)
    {
        ArgumentNullException.ThrowIfNull(@namespace);
        return _byNamespace.TryGetValue(@namespace, out var template) ? template : null;
    }

    /// <inheritdoc />
    public IEnumerable<IResourceTemplate> GetAllTemplates() => _bySourceType.Values;

    /// <inheritdoc />
    public void Register(IResourceTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (string.IsNullOrWhiteSpace(template.SourceType))
        {
            throw new ArgumentException(
                "Template SourceType cannot be null or empty",
                nameof(template));
        }

        if (string.IsNullOrWhiteSpace(template.Namespace))
        {
            throw new ArgumentException(
                "Template Namespace cannot be null or empty",
                nameof(template));
        }

        // Try to add to sourceType index first
        if (!_bySourceType.TryAdd(template.SourceType, template))
        {
            throw new InvalidOperationException(
                $"Resource template for sourceType '{template.SourceType}' is already registered");
        }

        // Try to add to namespace index
        if (!_byNamespace.TryAdd(template.Namespace, template))
        {
            // Rollback the sourceType registration to maintain consistency
            _bySourceType.TryRemove(template.SourceType, out _);

            throw new InvalidOperationException(
                $"Resource template namespace '{template.Namespace}' conflicts with existing template. " +
                $"Namespace is already registered for sourceType '{_byNamespace[template.Namespace].SourceType}'");
        }

        _logger.LogDebug(
            "Registered resource template {SourceType} with namespace {Namespace} ({PathCount} paths)",
            template.SourceType,
            template.Namespace,
            template.ValidPaths.Count);
    }

    /// <inheritdoc />
    public bool HasTemplate(string sourceType)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        return _bySourceType.ContainsKey(sourceType);
    }
}
