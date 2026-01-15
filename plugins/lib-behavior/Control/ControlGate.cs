// =============================================================================
// Control Gate Implementation
// Per-entity control gating for Intent Channel access.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Behavior.Control;

/// <summary>
/// Per-entity control gate implementation.
/// </summary>
public sealed class ControlGate : IControlGate
{
    private readonly object _lock = new();
    private readonly ILogger<ControlGate>? _logger;
    private readonly HashSet<string> _behaviorInputChannels;

    private ControlSource _currentSource;
    private ControlOptions? _currentOptions;
    private DateTime? _controlExpiry;

    /// <summary>
    /// Creates a new control gate for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="logger">Optional logger.</param>
    public ControlGate(Guid entityId, ILogger<ControlGate>? logger = null)
    {
        EntityId = entityId;
        _logger = logger;
        _currentSource = ControlSource.Behavior;
        _currentOptions = ControlOptions.ForBehavior();
        _behaviorInputChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public Guid EntityId { get; }

    /// <inheritdoc/>
    public ControlSource CurrentSource
    {
        get
        {
            lock (_lock)
            {
                // Check for expiry
                if (_controlExpiry.HasValue && DateTime.UtcNow > _controlExpiry.Value)
                {
                    // Control expired, return to behavior
                    ResetToBehaviorInternal();
                }

                return _currentSource;
            }
        }
    }

    /// <inheritdoc/>
    public ControlOptions? CurrentOptions
    {
        get
        {
            lock (_lock)
            {
                return _currentOptions;
            }
        }
    }

    /// <inheritdoc/>
    public bool AcceptsBehaviorOutput
    {
        get
        {
            lock (_lock)
            {
                return _currentSource == ControlSource.Behavior;
            }
        }
    }

    /// <inheritdoc/>
    public bool AcceptsPlayerInput
    {
        get
        {
            lock (_lock)
            {
                return _currentSource <= ControlSource.Player;
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlySet<string> BehaviorInputChannels
    {
        get
        {
            lock (_lock)
            {
                return _behaviorInputChannels.ToHashSet();
            }
        }
    }

    /// <inheritdoc/>
    public event EventHandler<ControlChangedEvent>? ControlChanged;

    /// <inheritdoc/>
    public async Task<bool> TakeControlAsync(ControlOptions options)
    {

        // Yield to ensure proper async pattern per IMPLEMENTATION TENETS (T23)
        await Task.Yield();

        lock (_lock)
        {
            // Check priority - can only take control if new source has higher or equal priority
            if (options.Source < _currentSource)
            {
                _logger?.LogDebug(
                    "Control request denied for entity {EntityId}: {NewSource} < current {CurrentSource}",
                    EntityId,
                    options.Source,
                    _currentSource);
                return false;
            }

            var previousSource = _currentSource;
            _currentSource = options.Source;
            _currentOptions = options;

            // Set expiry if duration specified
            _controlExpiry = options.Duration.HasValue
                ? DateTime.UtcNow + options.Duration.Value
                : null;

            // Update behavior input channels
            _behaviorInputChannels.Clear();
            if (options.AllowBehaviorInput != null)
            {
                foreach (var channel in options.AllowBehaviorInput)
                {
                    _behaviorInputChannels.Add(channel);
                }
            }

            _logger?.LogDebug(
                "Entity {EntityId} control changed: {PreviousSource} -> {NewSource}",
                EntityId,
                previousSource,
                options.Source);

            // Raise event
            RaiseControlChanged(previousSource, options.Source, null);

            return true;
        }
    }

    /// <inheritdoc/>
    public async Task ReturnControlAsync(ControlHandoff handoff)
    {

        // Yield to ensure proper async pattern per IMPLEMENTATION TENETS (T23)
        await Task.Yield();

        lock (_lock)
        {
            var previousSource = _currentSource;

            // Reset to behavior
            ResetToBehaviorInternal();

            _logger?.LogDebug(
                "Entity {EntityId} control returned: {PreviousSource} -> Behavior (style: {Style})",
                EntityId,
                previousSource,
                handoff.Style);

            // Raise event with handoff info
            RaiseControlChanged(previousSource, ControlSource.Behavior, handoff);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<IntentEmission> FilterEmissions(
        IReadOnlyList<IntentEmission> emissions,
        ControlSource source)
    {
        if (emissions.Count == 0)
        {
            return emissions;
        }

        lock (_lock)
        {
            // Cinematic source always passes
            if (source == ControlSource.Cinematic)
            {
                return emissions;
            }

            // If current source is behavior, all behavior emissions pass
            if (_currentSource == ControlSource.Behavior && source == ControlSource.Behavior)
            {
                return emissions;
            }

            // If current source is player, player and behavior emissions pass
            if (_currentSource == ControlSource.Player &&
                (source == ControlSource.Player || source == ControlSource.Behavior))
            {
                return emissions;
            }

            // During cinematic, only allow behavior on designated channels
            if (_currentSource == ControlSource.Cinematic && source == ControlSource.Behavior)
            {
                if (_behaviorInputChannels.Count == 0)
                {
                    return Array.Empty<IntentEmission>();
                }

                var filtered = new List<IntentEmission>();
                foreach (var emission in emissions)
                {
                    if (_behaviorInputChannels.Contains(emission.Channel))
                    {
                        filtered.Add(emission);
                    }
                }

                return filtered;
            }

            // Default: reject all
            return Array.Empty<IntentEmission>();
        }
    }

    private void ResetToBehaviorInternal()
    {
        _currentSource = ControlSource.Behavior;
        _currentOptions = ControlOptions.ForBehavior();
        _controlExpiry = null;
        _behaviorInputChannels.Clear();
    }

    private void RaiseControlChanged(ControlSource previous, ControlSource current, ControlHandoff? handoff)
    {
        var evt = new ControlChangedEvent(EntityId, previous, current, handoff);
        ControlChanged?.Invoke(this, evt);
    }
}
