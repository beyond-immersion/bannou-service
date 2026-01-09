using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.Mapping;

/// <summary>
/// Partial class for MappingService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
/// <remarks>
/// <para>
/// The Mapping service uses a dynamic subscription model for ingest events.
/// Rather than static event consumers, the service subscribes to per-channel
/// ingest topics (map.ingest.{channelId}) when authority is granted.
/// </para>
/// <para>
/// <b>Dynamic Subscriptions:</b>
/// <list type="bullet">
///   <item>SubscribeToIngestTopicAsync - Creates subscription when channel created</item>
///   <item>HandleIngestEventAsync - Processes ingest payloads from authority</item>
///   <item>Subscriptions are disposed when authority is released</item>
/// </list>
/// </para>
/// <para>
/// <b>Published Events:</b>
/// <list type="bullet">
///   <item>mapping.channel.created - When a new map channel is created</item>
///   <item>mapping.channel.updated - When a map channel is updated</item>
///   <item>mapping.channel.deleted - When a map channel is deleted</item>
///   <item>map.{regionId}.{kind}.updated - When map data is updated</item>
///   <item>map.{regionId}.{kind}.snapshot - When a full snapshot is published</item>
///   <item>map.{regionId}.{kind}.objects.changed - When objects are changed</item>
///   <item>map.warnings.unauthorized_publish - When non-authority publish detected</item>
/// </list>
/// </para>
/// </remarks>
public partial class MappingService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    /// <remarks>
    /// The Mapping service does not use static event consumers.
    /// Ingest event subscriptions are managed dynamically per-channel via
    /// SubscribeToIngestTopicAsync in MappingService.cs.
    /// </remarks>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // No static event consumers required.
        // Ingest subscriptions are managed dynamically:
        // - SubscribeToIngestTopicAsync() creates subscription when channel is created
        // - HandleIngestEventAsync() processes ingest events from authority
        // - Subscriptions are disposed when authority is released
        //
        // This dynamic model allows the service to handle arbitrary numbers of
        // ingest channels without pre-registering all possible topics.
    }
}
