// =============================================================================
// Behavior Stack
// Manages a stack of behavior layers for an entity with priority-based merging.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Behavior.Stack;

/// <summary>
/// Implementation of the behavior stack for an entity.
/// </summary>
/// <remarks>
/// <para>
/// The behavior stack manages multiple behavior layers that all contribute
/// to the entity's final output. Each layer produces IntentEmissions, and
/// the stack merges them using the archetype's per-channel merge strategies.
/// </para>
/// <para>
/// Key features:
/// - Layers organized by category (Base, Cultural, Professional, Personal, Situational)
/// - Priority-based ordering within categories
/// - All active layers evaluated every frame
/// - Per-channel merge using archetype-defined strategies
/// </para>
/// </remarks>
public sealed class BehaviorStack : IBehaviorStack
{
    private readonly List<IBehaviorLayer> _layers;
    private readonly IIntentStackMerger _merger;
    private readonly ILogger<BehaviorStack>? _logger;
    private readonly object _lock = new();

    /// <summary>
    /// Creates a new behavior stack for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="archetype">The entity's archetype.</param>
    /// <param name="merger">The merger for combining layer outputs.</param>
    /// <param name="logger">Optional logger.</param>
    public BehaviorStack(
        Guid entityId,
        IArchetypeDefinition archetype,
        IIntentStackMerger merger,
        ILogger<BehaviorStack>? logger = null)
    {
        EntityId = entityId;
        Archetype = archetype ?? throw new ArgumentNullException(nameof(archetype));
        _merger = merger ?? throw new ArgumentNullException(nameof(merger));
        _logger = logger;
        _layers = new List<IBehaviorLayer>();
    }

    /// <inheritdoc/>
    public Guid EntityId { get; }

    /// <inheritdoc/>
    public IArchetypeDefinition Archetype { get; }

    /// <inheritdoc/>
    public IReadOnlyList<IBehaviorLayer> Layers
    {
        get
        {
            lock (_lock)
            {
                return _layers.ToList().AsReadOnly();
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<IBehaviorLayer> ActiveLayers
    {
        get
        {
            lock (_lock)
            {
                return _layers.Where(l => l.IsActive).ToList().AsReadOnly();
            }
        }
    }

    /// <inheritdoc/>
    public event EventHandler<BehaviorLayerEventArgs>? LayerAdded;

    /// <inheritdoc/>
    public event EventHandler<BehaviorLayerEventArgs>? LayerRemoved;

    /// <inheritdoc/>
    public event EventHandler<BehaviorLayerEventArgs>? LayerActivated;

    /// <inheritdoc/>
    public event EventHandler<BehaviorLayerEventArgs>? LayerDeactivated;

    /// <inheritdoc/>
    public void AddLayer(IBehaviorLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        lock (_lock)
        {
            // Check for duplicate ID
            if (_layers.Any(l => l.Id == layer.Id))
            {
                throw new ArgumentException($"Layer with ID '{layer.Id}' already exists", nameof(layer));
            }

            _layers.Add(layer);
            SortLayers();

            _logger?.LogDebug(
                "Added layer {LayerId} ({Category}, priority {Priority}) to entity {EntityId}",
                layer.Id,
                layer.Category,
                layer.Priority,
                EntityId);
        }

        LayerAdded?.Invoke(this, new BehaviorLayerEventArgs(layer));
    }

    /// <inheritdoc/>
    public bool RemoveLayer(string layerId)
    {
        if (string.IsNullOrEmpty(layerId))
        {
            return false;
        }

        IBehaviorLayer? removed = null;

        lock (_lock)
        {
            var index = _layers.FindIndex(l => l.Id == layerId);
            if (index >= 0)
            {
                removed = _layers[index];
                _layers.RemoveAt(index);

                _logger?.LogDebug(
                    "Removed layer {LayerId} from entity {EntityId}",
                    layerId,
                    EntityId);
            }
        }

        if (removed != null)
        {
            LayerRemoved?.Invoke(this, new BehaviorLayerEventArgs(removed));
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public IBehaviorLayer? GetLayer(string layerId)
    {
        if (string.IsNullOrEmpty(layerId))
        {
            return null;
        }

        lock (_lock)
        {
            return _layers.Find(l => l.Id == layerId);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<IBehaviorLayer> GetLayersByCategory(BehaviorCategory category)
    {
        lock (_lock)
        {
            return _layers
                .Where(l => l.Category == category)
                .OrderByDescending(l => l.Priority)
                .ToList()
                .AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public bool ActivateLayer(string layerId)
    {
        var layer = GetLayer(layerId);
        if (layer == null)
        {
            return false;
        }

        if (layer.IsActive)
        {
            return true;
        }

        layer.Activate();

        _logger?.LogDebug(
            "Activated layer {LayerId} for entity {EntityId}",
            layerId,
            EntityId);

        LayerActivated?.Invoke(this, new BehaviorLayerEventArgs(layer));
        return true;
    }

    /// <inheritdoc/>
    public bool DeactivateLayer(string layerId)
    {
        var layer = GetLayer(layerId);
        if (layer == null)
        {
            return false;
        }

        if (!layer.IsActive)
        {
            return true;
        }

        layer.Deactivate();

        _logger?.LogDebug(
            "Deactivated layer {LayerId} for entity {EntityId}",
            layerId,
            EntityId);

        LayerDeactivated?.Invoke(this, new BehaviorLayerEventArgs(layer));
        return true;
    }

    /// <inheritdoc/>
    public async ValueTask<BehaviorStackOutput> EvaluateAsync(
        BehaviorEvaluationContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var output = new BehaviorStackOutput(EntityId);

        // Get active layers snapshot
        IReadOnlyList<IBehaviorLayer> activeLayers;
        lock (_lock)
        {
            activeLayers = _layers.Where(l => l.IsActive).ToList();
        }

        if (activeLayers.Count == 0)
        {
            _logger?.LogDebug(
                "No active layers for entity {EntityId}, returning empty output",
                EntityId);
            return output;
        }

        // Evaluate all active layers and collect contributions
        var allContributions = new List<IntentContribution>();

        foreach (var layer in activeLayers)
        {
            try
            {
                var emissions = await layer.EvaluateAsync(context, ct);

                foreach (var emission in emissions)
                {
                    var contribution = new IntentContribution(
                        layer.Id,
                        layer.Priority,
                        layer.Category,
                        emission);
                    allContributions.Add(contribution);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "Error evaluating layer {LayerId} for entity {EntityId}",
                    layer.Id,
                    EntityId);
                // Continue with other layers
            }
        }

        output.AllContributions.AddRange(allContributions);

        // Merge contributions per channel
        var merged = _merger.MergeAll(allContributions, Archetype);

        foreach (var kvp in merged)
        {
            output.MergedEmissions[kvp.Key] = kvp.Value;

            // Track winning layer
            var winner = FindWinningLayer(kvp.Key, allContributions, kvp.Value);
            if (winner != null)
            {
                output.WinningLayers[kvp.Key] = winner;
            }
        }

        _logger?.LogDebug(
            "Evaluated {LayerCount} active layers for entity {EntityId}, " +
            "produced {EmissionCount} merged emissions from {ContributionCount} contributions",
            activeLayers.Count,
            EntityId,
            output.MergedEmissions.Count,
            allContributions.Count);

        return output;
    }

    /// <inheritdoc/>
    public void Clear()
    {
        List<IBehaviorLayer> removed;

        lock (_lock)
        {
            removed = new List<IBehaviorLayer>(_layers);
            _layers.Clear();

            _logger?.LogDebug(
                "Cleared {Count} layers from entity {EntityId}",
                removed.Count,
                EntityId);
        }

        foreach (var layer in removed)
        {
            LayerRemoved?.Invoke(this, new BehaviorLayerEventArgs(layer));
        }
    }

    /// <summary>
    /// Sorts layers by category (ascending) then priority (descending within category).
    /// </summary>
    private void SortLayers()
    {
        _layers.Sort((a, b) =>
        {
            var categoryCompare = a.Category.CompareTo(b.Category);
            if (categoryCompare != 0)
            {
                return categoryCompare;
            }
            // Higher priority first within category
            return b.Priority.CompareTo(a.Priority);
        });
    }

    /// <summary>
    /// Finds which layer contributed the winning emission for a channel.
    /// </summary>
    private static string? FindWinningLayer(
        string channel,
        List<IntentContribution> contributions,
        IntentEmission mergedEmission)
    {
        // For Priority strategy, the winner is the highest priority contribution
        // For Blend strategy, we report the highest urgency contributor
        var channelContributions = contributions
            .Where(c => c.Emission.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (channelContributions.Count == 0)
        {
            return null;
        }

        // Return the layer with highest effective priority among contributors
        return channelContributions
            .OrderByDescending(c => c.EffectivePriority)
            .ThenByDescending(c => c.Emission.Urgency)
            .First()
            .LayerId;
    }
}
