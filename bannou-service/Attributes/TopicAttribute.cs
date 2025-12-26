namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Stub Topic attribute for backward compatibility.
/// This replaces Dapr's [Topic] attribute after Dapr removal.
/// Actual pub/sub is handled by MassTransit consumers in lib-messaging.
/// </summary>
/// <remarks>
/// This attribute has no runtime effect - it exists only to allow
/// compilation of legacy event controllers that used Dapr's [Topic].
/// For new pub/sub handling, use MassTransit IConsumer pattern.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class TopicAttribute : Attribute
{
    /// <summary>
    /// The name of the pub/sub component (legacy, not used).
    /// </summary>
    public string PubsubName { get; }

    /// <summary>
    /// The topic name to subscribe to (legacy, not used).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Creates a new Topic attribute.
    /// </summary>
    /// <param name="pubsubName">The pub/sub component name.</param>
    /// <param name="name">The topic name.</param>
    public TopicAttribute(string pubsubName, string name)
    {
        PubsubName = pubsubName;
        Name = name;
    }
}
