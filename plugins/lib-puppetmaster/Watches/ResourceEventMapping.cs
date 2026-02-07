// =============================================================================
// Resource Event Mapping
// Maps resource types to their lifecycle event topics for watch subscriptions.
// Mappings are loaded from the generated ResourceEventMappings class.
// =============================================================================

namespace BeyondImmersion.BannouService.Puppetmaster.Watches;

/// <summary>
/// Maps resource types to their lifecycle event topics.
/// Configured at startup from schema-generated mappings, immutable during runtime.
/// </summary>
/// <remarks>
/// <para>
/// This mapping enables the watch system to know which RabbitMQ topics to subscribe to
/// for each resource type, and how to extract the resource ID from received events.
/// </para>
/// <para>
/// Mappings are loaded from <see cref="ResourceEventMappings"/> which is generated
/// from x-resource-mapping extensions in event schemas.
/// </para>
/// </remarks>
public sealed class ResourceEventMapping
{
    private readonly Dictionary<string, SourceTypeMapping> _sourceToMapping = new();
    private readonly Dictionary<string, List<string>> _resourceToSources = new();

    /// <summary>
    /// Creates a new resource event mapping populated from schema-generated mappings.
    /// </summary>
    public ResourceEventMapping()
    {
        // Load mappings from the schema-generated registry
        foreach (var entry in ResourceEventMappings.All)
        {
            AddMapping(
                entry.SourceType,
                entry.EventTopic,
                entry.ResourceIdField,
                entry.ResourceType,
                entry.IsDeletion);
        }
    }

    /// <summary>
    /// Adds a source type mapping.
    /// </summary>
    /// <param name="sourceType">The source type identifier (e.g., "character-personality").</param>
    /// <param name="eventTopic">The RabbitMQ event topic (e.g., "personality.updated").</param>
    /// <param name="resourceIdField">The JSON field name containing the resource ID.</param>
    /// <param name="resourceType">The resource type this source belongs to.</param>
    /// <param name="isDeletion">True if this event indicates the resource was deleted.</param>
    public void AddMapping(
        string sourceType,
        string eventTopic,
        string resourceIdField,
        string resourceType,
        bool isDeletion = false)
    {
        _sourceToMapping[sourceType] = new SourceTypeMapping(eventTopic, resourceIdField, resourceType, isDeletion);

        if (!_resourceToSources.TryGetValue(resourceType, out var sources))
        {
            sources = new List<string>();
            _resourceToSources[resourceType] = sources;
        }
        if (!sources.Contains(sourceType))
        {
            sources.Add(sourceType);
        }
    }

    /// <summary>
    /// Gets all source types for a resource type.
    /// </summary>
    /// <param name="resourceType">The resource type (e.g., "character").</param>
    /// <returns>List of source types for this resource.</returns>
    public IReadOnlyList<string> GetSourcesForResource(string resourceType)
    {
        return _resourceToSources.TryGetValue(resourceType, out var sources)
            ? sources
            : [];
    }

    /// <summary>
    /// Gets the mapping for a source type.
    /// </summary>
    /// <param name="sourceType">The source type.</param>
    /// <returns>The mapping if found, null otherwise.</returns>
    public SourceTypeMapping? GetMapping(string sourceType)
    {
        return _sourceToMapping.TryGetValue(sourceType, out var mapping) ? mapping : null;
    }

    /// <summary>
    /// Gets all unique event topics that need to be subscribed to.
    /// </summary>
    /// <returns>Collection of event topics.</returns>
    public IEnumerable<string> GetAllTopics()
    {
        return _sourceToMapping.Values.Select(m => m.EventTopic).Distinct();
    }

    /// <summary>
    /// Gets all source types that map to a specific event topic.
    /// </summary>
    /// <param name="eventTopic">The event topic.</param>
    /// <returns>Source types that use this topic.</returns>
    public IEnumerable<string> GetSourceTypesForTopic(string eventTopic)
    {
        return _sourceToMapping
            .Where(kvp => kvp.Value.EventTopic == eventTopic)
            .Select(kvp => kvp.Key);
    }
}

/// <summary>
/// Mapping information for a source type.
/// </summary>
/// <param name="EventTopic">The RabbitMQ event topic to subscribe to.</param>
/// <param name="ResourceIdField">The JSON field name containing the resource ID.</param>
/// <param name="ResourceType">The resource type this source belongs to.</param>
/// <param name="IsDeletion">True if this event indicates the resource was deleted.</param>
public sealed record SourceTypeMapping(
    string EventTopic,
    string ResourceIdField,
    string ResourceType,
    bool IsDeletion = false);
