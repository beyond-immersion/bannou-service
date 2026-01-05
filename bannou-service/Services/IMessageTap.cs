#nullable enable

using BeyondImmersion.BannouService.Messaging;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Creates taps that forward events from a source topic to a destination.
/// A tap subscribes to events on a source topic and forwards them to a
/// different exchange/routing key, allowing multiple consumers to receive
/// copies of the same event stream.
/// </summary>
/// <remarks>
/// <para>
/// This interface is designed for scenarios like character event tapping,
/// where a client session needs to receive the same events that NPC actors
/// normally receive for characters they control.
/// </para>
/// <para>
/// Example use case: When a player logs in and possesses characters,
/// their session taps the character event streams (character.events.{id})
/// so they receive status updates (damage, heals, perception events).
/// </para>
/// <para>
/// Each tap is independent - removing one does not affect others.
/// Multiple taps can forward to the same destination from different sources.
/// </para>
/// <para>
/// <b>EXTENSION POINT:</b> Future versions could add filtering options
/// to the tap creation, allowing only certain event types to be forwarded.
/// </para>
/// </remarks>
public interface IMessageTap
{
    /// <summary>
    /// Creates a tap that forwards events from a source topic to a destination.
    /// </summary>
    /// <param name="sourceTopic">The topic to subscribe to (e.g., "character.events.char-123")</param>
    /// <param name="destination">The destination exchange/routing configuration</param>
    /// <param name="sourceExchange">The source exchange to tap from (defaults to "bannou")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// A disposable tap handle. Dispose to remove the tap and stop forwarding.
    /// The returned <see cref="ITapHandle"/> provides the TapId for tracking.
    /// </returns>
    Task<ITapHandle> CreateTapAsync(
        string sourceTopic,
        TapDestination destination,
        string? sourceExchange = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Handle to an active tap. Disposing removes the tap and stops forwarding.
/// </summary>
public interface ITapHandle : IAsyncDisposable
{
    /// <summary>
    /// Unique identifier for this tap instance.
    /// Can be used to track which events came from which tap.
    /// </summary>
    Guid TapId { get; }

    /// <summary>
    /// The source topic being tapped.
    /// </summary>
    string SourceTopic { get; }

    /// <summary>
    /// The destination configuration for this tap.
    /// </summary>
    TapDestination Destination { get; }

    /// <summary>
    /// When this tap was created.
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Whether this tap is currently active.
    /// </summary>
    bool IsActive { get; }
}

/// <summary>
/// Configuration for where tapped events should be forwarded.
/// </summary>
/// <remarks>
/// <para>
/// The destination specifies the exchange, routing key, and exchange type
/// for forwarding tapped messages.
/// </para>
/// <para>
/// For client session tapping, typical configuration would be:
/// <list type="bullet">
/// <item>Exchange: "bannou-client-events"</item>
/// <item>RoutingKey: "CONNECT_SESSION_{sessionId}"</item>
/// <item>ExchangeType: Direct</item>
/// </list>
/// </para>
/// </remarks>
public sealed class TapDestination
{
    /// <summary>
    /// The exchange to forward tapped messages to.
    /// </summary>
    /// <example>bannou-client-events</example>
    public required string Exchange { get; init; }

    /// <summary>
    /// The routing key to use when forwarding messages.
    /// For direct exchanges, this determines which queue receives the message.
    /// </summary>
    /// <example>CONNECT_SESSION_abc123</example>
    public required string RoutingKey { get; init; }

    /// <summary>
    /// The type of exchange to use for forwarding (defaults to Topic for service events).
    /// </summary>
    public TapExchangeType ExchangeType { get; init; } = TapExchangeType.Topic;

    /// <summary>
    /// Whether to create the destination exchange if it doesn't exist.
    /// Defaults to true for convenience.
    /// </summary>
    public bool CreateExchangeIfNotExists { get; init; } = true;
}

/// <summary>
/// Exchange types supported for tap destinations.
/// Maps to RabbitMQ exchange types.
/// </summary>
public enum TapExchangeType
{
    /// <summary>
    /// Fanout exchange - broadcasts to all bound queues.
    /// Use for broadcast scenarios where all listeners should receive copies.
    /// </summary>
    Fanout,

    /// <summary>
    /// Direct exchange - routes by exact routing key match.
    /// Use for client events where specific sessions should receive messages.
    /// </summary>
    Direct,

    /// <summary>
    /// Topic exchange - routes by routing key pattern (default for service events).
    /// Use for service events and hierarchical routing patterns (e.g., "character.*.combat").
    /// </summary>
    Topic
}
