#nullable enable

using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// In-memory implementation of IMessageTap for testing and minimal infrastructure scenarios.
/// Works with <see cref="InMemoryMessageBus"/> to provide local tap functionality.
/// </summary>
/// <remarks>
/// <para>
/// This implementation subscribes to topics on the in-memory bus and forwards
/// messages by publishing tapped envelopes to the destination topic.
/// </para>
/// <para>
/// Since InMemoryMessageBus doesn't have real exchanges, the destination
/// exchange/routing is simulated by publishing to a combined topic.
/// </para>
/// </remarks>
public sealed class InMemoryMessageTap : IMessageTap, IAsyncDisposable
{
    private readonly InMemoryMessageBus _messageBus;
    private readonly ILogger<InMemoryMessageTap> _logger;
    private readonly ConcurrentDictionary<Guid, TapHandleImpl> _activeTaps = new();

    /// <summary>
    /// Creates a new InMemoryMessageTap instance.
    /// </summary>
    public InMemoryMessageTap(
        InMemoryMessageBus messageBus,
        ILogger<InMemoryMessageTap> logger)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogWarning("InMemoryMessageTap initialized - taps will only work in-process");
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
        var effectiveSourceExchange = sourceExchange ?? "bannou";
        var createdAt = DateTimeOffset.UtcNow;

        // In-memory: combine exchange and routing key as the destination topic
        var destinationTopic = $"{destination.Exchange}.{destination.RoutingKey}";

        _logger.LogDebug(
            "Creating tap {TapId} from {SourceTopic} to {DestinationTopic}",
            tapId,
            sourceTopic,
            destinationTopic);

        // Create dynamic subscription that forwards to destination
        var subscription = await _messageBus.SubscribeDynamicAsync<IBannouEvent>(
            sourceTopic,
            async (bannouEvent, ct) =>
            {
                await ForwardMessageAsync(
                    bannouEvent,
                    tapId,
                    sourceTopic,
                    effectiveSourceExchange,
                    destination,
                    destinationTopic,
                    createdAt,
                    ct);
            },
            cancellationToken);

        var tapHandle = new TapHandleImpl(
            tapId,
            sourceTopic,
            destination,
            createdAt,
            subscription,
            this);

        _activeTaps[tapId] = tapHandle;

        _logger.LogInformation(
            "Created tap {TapId} from {SourceTopic} to {DestinationTopic}",
            tapId,
            sourceTopic,
            destinationTopic);

        return tapHandle;
    }

    private async Task ForwardMessageAsync(
        IBannouEvent bannouEvent,
        Guid tapId,
        string sourceTopic,
        string sourceExchange,
        TapDestination destination,
        string destinationTopic,
        DateTimeOffset tapCreatedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            // Create tapped envelope with routing metadata
            var tappedEnvelope = new TappedMessageEnvelope(
                bannouEvent,
                tapId,
                sourceTopic,
                sourceExchange,
                destination.Exchange,
                destination.RoutingKey,
                destination.ExchangeType.ToString().ToLowerInvariant(),
                tapCreatedAt);

            // Publish to destination topic
            await _messageBus.PublishAsync(destinationTopic, tappedEnvelope, null, cancellationToken);

            _logger.LogDebug(
                "Tap {TapId} forwarded message {EventId} from {SourceTopic} to {DestinationTopic}",
                tapId,
                bannouEvent.EventId,
                sourceTopic,
                destinationTopic);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Tap {TapId} failed to forward message {EventId} from {SourceTopic}",
                tapId,
                bannouEvent.EventId,
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
                await tap.Subscription.DisposeAsync();
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
                await tap.Subscription.DisposeAsync();
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
    /// Internal implementation of ITapHandle for in-memory taps.
    /// </summary>
    private sealed class TapHandleImpl : ITapHandle
    {
        private readonly InMemoryMessageTap _parent;
        private bool _isActive = true;

        public Guid TapId { get; }
        public string SourceTopic { get; }
        public TapDestination Destination { get; }
        public DateTimeOffset CreatedAt { get; }
        public bool IsActive => _isActive;
        public IAsyncDisposable Subscription { get; }

        public TapHandleImpl(
            Guid tapId,
            string sourceTopic,
            TapDestination destination,
            DateTimeOffset createdAt,
            IAsyncDisposable subscription,
            InMemoryMessageTap parent)
        {
            TapId = tapId;
            SourceTopic = sourceTopic;
            Destination = destination;
            CreatedAt = createdAt;
            Subscription = subscription;
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
