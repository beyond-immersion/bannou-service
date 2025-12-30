#nullable enable

using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using MassTransit;
using MassTransit.RabbitMqTransport;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// MassTransit-based implementation of IMessageTap.
/// Creates taps that subscribe to source topics and forward messages to destinations.
/// </summary>
/// <remarks>
/// <para>
/// Each tap creates a dynamic subscription to the source topic that forwards
/// all received messages to the destination exchange with the configured routing.
/// </para>
/// <para>
/// The implementation uses MassTransit's dynamic receive endpoint feature
/// for the source subscription and direct publishing for the forwarding.
/// </para>
/// </remarks>
public sealed class MassTransitMessageTap : IMessageTap, IAsyncDisposable
{
    private readonly IBusControl _busControl;
    private readonly ILogger<MassTransitMessageTap> _logger;
    private readonly MessagingServiceConfiguration _configuration;
    private readonly ConcurrentDictionary<Guid, TapHandleImpl> _activeTaps = new();

    /// <summary>
    /// Creates a new MassTransitMessageTap instance.
    /// </summary>
    public MassTransitMessageTap(
        IBusControl busControl,
        ILogger<MassTransitMessageTap> logger,
        MessagingServiceConfiguration configuration)
    {
        _busControl = busControl ?? throw new ArgumentNullException(nameof(busControl));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc/>
    public async Task<ITapHandle> CreateTapAsync(
        string sourceTopic,
        TapDestination destination,
        string? sourceExchange = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceTopic);
        ArgumentNullException.ThrowIfNull(destination);

        var tapId = Guid.NewGuid();
        var effectiveSourceExchange = sourceExchange ?? _configuration.DefaultExchange;
        var createdAt = DateTimeOffset.UtcNow;

        // Create a unique queue name for this tap's source subscription
        var tapQueueName = $"tap.{tapId:N}.{sourceTopic}";

        _logger.LogDebug(
            "Creating tap {TapId} from {SourceExchange}/{SourceTopic} to {DestExchange}/{DestRoutingKey} ({DestType})",
            tapId,
            effectiveSourceExchange,
            sourceTopic,
            destination.Exchange,
            destination.RoutingKey,
            destination.ExchangeType);

        try
        {
            // Create receive endpoint that subscribes to the source and forwards to destination
            var handle = _busControl.ConnectReceiveEndpoint(tapQueueName, endpoint =>
            {
                endpoint.PrefetchCount = _configuration.DefaultPrefetchCount;

                // Disable MassTransit's automatic consume topology - we want to receive
                // directly from our bound exchange without type-based routing
                endpoint.ConfigureConsumeTopology = false;

                // Bind to source exchange for receiving messages
                // Uses Fanout exchange type - each tapped source (e.g., character event stream)
                // is its own fanout exchange that broadcasts all events to bound queues
                if (endpoint is IRabbitMqReceiveEndpointConfigurator rmqEndpoint)
                {
                    rmqEndpoint.Bind(effectiveSourceExchange, x =>
                    {
                        x.RoutingKey = sourceTopic;
                        x.ExchangeType = ExchangeType.Fanout;
                    });

                    _logger.LogDebug(
                        "Tap {TapId}: Bound queue '{QueueName}' to source exchange '{Exchange}' with routing key '{RoutingKey}'",
                        tapId, tapQueueName, effectiveSourceExchange, sourceTopic);
                }
                else
                {
                    _logger.LogWarning(
                        "Tap {TapId}: Cannot bind queue to exchange - endpoint is not RabbitMQ. Tap may not receive messages.",
                        tapId);
                }

                // Handler that forwards received messages to the destination
                // Uses IBannouEvent interface to receive any Bannou event type
                endpoint.Handler<IBannouEvent>(async context =>
                {
                    await ForwardMessageAsync(
                        context,
                        tapId,
                        sourceTopic,
                        effectiveSourceExchange,
                        destination,
                        createdAt,
                        context.CancellationToken);
                });
            });

            await handle.Ready;

            var tapHandle = new TapHandleImpl(
                tapId,
                sourceTopic,
                destination,
                createdAt,
                handle,
                this);

            _activeTaps[tapId] = tapHandle;

            _logger.LogInformation(
                "Created tap {TapId} from {SourceExchange}/{SourceTopic} to {DestExchange}/{DestRoutingKey}",
                tapId,
                effectiveSourceExchange,
                sourceTopic,
                destination.Exchange,
                destination.RoutingKey);

            return tapHandle;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create tap {TapId} from {SourceTopic} to {DestExchange}/{DestRoutingKey}",
                tapId,
                sourceTopic,
                destination.Exchange,
                destination.RoutingKey);
            throw;
        }
    }

    private async Task ForwardMessageAsync(
        ConsumeContext<IBannouEvent> context,
        Guid tapId,
        string sourceTopic,
        string sourceExchange,
        TapDestination destination,
        DateTimeOffset tapCreatedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            // Create tapped envelope with routing metadata
            var tappedEnvelope = new TappedMessageEnvelope(
                context.Message,
                tapId,
                sourceTopic,
                sourceExchange,
                destination.Exchange,
                destination.RoutingKey,
                destination.ExchangeType.ToString().ToLowerInvariant(),
                tapCreatedAt);

            // Build destination URI based on exchange type
            var destExchangeType = destination.ExchangeType switch
            {
                TapExchangeType.Direct => "direct",
                TapExchangeType.Topic => "topic",
                _ => "fanout"
            };

            var endpointUri = new Uri($"exchange:{destination.Exchange}?type={destExchangeType}");
            var endpoint = await _busControl.GetSendEndpoint(endpointUri);

            await endpoint.Send(tappedEnvelope, ctx =>
            {
                ctx.MessageId = Guid.NewGuid();

                // Set routing key for direct/topic exchanges
                if (destination.ExchangeType != TapExchangeType.Fanout &&
                    !string.IsNullOrEmpty(destination.RoutingKey))
                {
                    ctx.SetRoutingKey(destination.RoutingKey);
                }

                ctx.Durable = true;
            }, cancellationToken);

            _logger.LogDebug(
                "Tap {TapId}: Forwarded message {EventId} from {SourceTopic} to {DestExchange}/{DestRoutingKey}",
                tapId,
                context.Message.BannouEventId,
                sourceTopic,
                destination.Exchange,
                destination.RoutingKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Tap {TapId}: Failed to forward message {EventId} from {SourceTopic}",
                tapId,
                context.Message.BannouEventId,
                sourceTopic);
            throw;
        }
    }

    /// <summary>
    /// Removes a tap by its ID.
    /// </summary>
    internal async Task RemoveTapAsync(Guid tapId)
    {
        if (_activeTaps.TryRemove(tapId, out var tap))
        {
            try
            {
                await tap.EndpointHandle.StopAsync();
                tap.MarkInactive();

                _logger.LogInformation(
                    "Removed tap {TapId} from {SourceTopic}",
                    tapId,
                    tap.SourceTopic);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error removing tap {TapId}",
                    tapId);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var (tapId, tap) in _activeTaps)
        {
            try
            {
                await tap.EndpointHandle.StopAsync();
                tap.MarkInactive();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing tap {TapId}", tapId);
            }
        }
        _activeTaps.Clear();
    }

    /// <summary>
    /// Internal implementation of ITapHandle.
    /// </summary>
    private sealed class TapHandleImpl : ITapHandle
    {
        private readonly MassTransitMessageTap _parent;
        private bool _isActive = true;

        public Guid TapId { get; }
        public string SourceTopic { get; }
        public TapDestination Destination { get; }
        public DateTimeOffset CreatedAt { get; }
        public bool IsActive => _isActive;
        public HostReceiveEndpointHandle EndpointHandle { get; }

        public TapHandleImpl(
            Guid tapId,
            string sourceTopic,
            TapDestination destination,
            DateTimeOffset createdAt,
            HostReceiveEndpointHandle endpointHandle,
            MassTransitMessageTap parent)
        {
            TapId = tapId;
            SourceTopic = sourceTopic;
            Destination = destination;
            CreatedAt = createdAt;
            EndpointHandle = endpointHandle;
            _parent = parent;
        }

        public void MarkInactive() => _isActive = false;

        public async ValueTask DisposeAsync()
        {
            if (_isActive)
            {
                await _parent.RemoveTapAsync(TapId);
            }
        }
    }
}
