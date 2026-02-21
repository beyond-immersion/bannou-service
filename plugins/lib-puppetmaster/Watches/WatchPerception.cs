// =============================================================================
// Watch Perception
// Perception payload for resource change notifications.
// =============================================================================

using System.Text.Json;

namespace BeyondImmersion.BannouService.Puppetmaster.Watches;

/// <summary>
/// Perception payload injected into an actor's bounded channel when a watched resource changes.
/// </summary>
/// <remarks>
/// <para>
/// This perception type follows the standard perception contract used by Actor service.
/// It carries the original lifecycle event data along with metadata about the watch.
/// </para>
/// <para>
/// Example ABML handling:
/// <code>
/// flows:
///   handle_perceptions:
///     triggers:
///       - event: perception
///     actions:
///       - cond:
///           - when: ${perception.type == 'watch:resource_changed'}
///             then:
///               - call: handle_resource_changed
/// </code>
/// </para>
/// </remarks>
public sealed class WatchPerception
{
    /// <summary>
    /// Perception type identifier. Always "watch:resource_changed" for resource changes,
    /// or "watch:resource_deleted" for deletion notifications.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Urgency of this perception (0.0-1.0). Resource changes default to 0.7.
    /// </summary>
    public float Urgency { get; init; } = 0.7f;

    /// <summary>
    /// Resource type (e.g., "character", "realm").
    /// </summary>
    public required string ResourceType { get; init; }

    /// <summary>
    /// Resource GUID that changed.
    /// </summary>
    public required Guid ResourceId { get; init; }

    /// <summary>
    /// Source type that triggered the change (e.g., "character-personality").
    /// </summary>
    public required string SourceType { get; init; }

    /// <summary>
    /// Original event topic (e.g., "personality.updated").
    /// </summary>
    public required string EventTopic { get; init; }

    /// <summary>
    /// Original lifecycle event data as JSON.
    /// </summary>
    public JsonElement? EventData { get; init; }

    /// <summary>
    /// True if this is a deletion notification (final perception before unwatch).
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>
    /// Timestamp when the change was detected.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a resource changed perception.
    /// </summary>
    public static WatchPerception ResourceChanged(
        string resourceType,
        Guid resourceId,
        string sourceType,
        string eventTopic,
        JsonElement? eventData) => new()
        {
            Type = "watch:resource_changed",
            ResourceType = resourceType,
            ResourceId = resourceId,
            SourceType = sourceType,
            EventTopic = eventTopic,
            EventData = eventData,
            Deleted = false
        };

    /// <summary>
    /// Creates a resource deleted perception.
    /// </summary>
    public static WatchPerception ResourceDeleted(
        string resourceType,
        Guid resourceId,
        string sourceType,
        string eventTopic) => new()
        {
            Type = "watch:resource_deleted",
            Urgency = 0.9f, // Higher urgency for deletions
            ResourceType = resourceType,
            ResourceId = resourceId,
            SourceType = sourceType,
            EventTopic = eventTopic,
            EventData = null,
            Deleted = true
        };
}
