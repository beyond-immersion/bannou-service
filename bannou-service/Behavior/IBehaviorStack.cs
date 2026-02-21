// ═══════════════════════════════════════════════════════════════════════════
// Behavior Stack Interfaces
// Core interfaces for the Behavior Stacking system (Layer 4).
// Implementations provided by lib-behavior plugin.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Archetypes;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Category of behavior layer in the stack.
/// </summary>
/// <remarks>
/// <para>
/// Categories provide semantic grouping for behaviors. Within a category,
/// layers are ordered by priority. Layers can be added/removed per category.
/// </para>
/// </remarks>
public enum BehaviorCategory
{
    /// <summary>
    /// Fundamental behaviors shared by entity type (e.g., humanoid-base).
    /// Lowest priority - always present but easily overridden.
    /// </summary>
    Base = 0,

    /// <summary>
    /// Cultural and regional behaviors (e.g., medieval-european).
    /// Influences speech patterns, gestures, social norms.
    /// </summary>
    Cultural = 1,

    /// <summary>
    /// Role-based behaviors (e.g., guard-patrol, merchant-trading).
    /// Defines primary occupation and default activities.
    /// </summary>
    Professional = 2,

    /// <summary>
    /// Individual personality traits (e.g., afraid-of-spiders, cheerful).
    /// Personal quirks and emotional tendencies.
    /// </summary>
    Personal = 3,

    /// <summary>
    /// Situational and temporary behaviors (e.g., combat-mode, fleeing).
    /// Highest priority - triggered by events or GOAP planning.
    /// </summary>
    Situational = 4
}

/// <summary>
/// A contribution from a behavior layer to an intent channel.
/// </summary>
/// <param name="LayerId">The layer that produced this contribution.</param>
/// <param name="LayerPriority">The priority of the layer within its category.</param>
/// <param name="Category">The category of the layer.</param>
/// <param name="Emission">The intent emission.</param>
public sealed record IntentContribution(
    string LayerId,
    int LayerPriority,
    BehaviorCategory Category,
    IntentEmission Emission)
{
    /// <summary>
    /// Computes effective priority based on category and layer priority.
    /// Higher values win during merge.
    /// </summary>
    /// <remarks>
    /// Formula: (category * 1000) + layerPriority
    /// This ensures category always trumps layer priority within a category.
    /// </remarks>
    public int EffectivePriority => ((int)Category * 1000) + LayerPriority;
}

/// <summary>
/// Context for evaluating a behavior layer.
/// </summary>
public sealed class BehaviorEvaluationContext
{
    /// <summary>
    /// Creates a new evaluation context.
    /// </summary>
    /// <param name="entityId">The entity being evaluated.</param>
    /// <param name="archetype">The entity's archetype.</param>
    public BehaviorEvaluationContext(Guid entityId, IArchetypeDefinition archetype)
    {
        EntityId = entityId;
        Archetype = archetype ?? throw new ArgumentNullException(nameof(archetype));
    }

    /// <summary>
    /// The entity ID being evaluated.
    /// </summary>
    public Guid EntityId { get; }

    /// <summary>
    /// The entity's archetype definition.
    /// </summary>
    public IArchetypeDefinition Archetype { get; }

    /// <summary>
    /// Current simulation time (for time-based behaviors).
    /// </summary>
    public TimeSpan SimulationTime { get; init; }

    /// <summary>
    /// Delta time since last evaluation.
    /// </summary>
    public TimeSpan DeltaTime { get; init; }

    /// <summary>
    /// Additional context data available to behaviors.
    /// </summary>
    public IReadOnlyDictionary<string, object> Data { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// Event raised when a layer wants to trigger a situational behavior.
    /// </summary>
    public Action<SituationalTriggerRequest>? OnSituationalTrigger { get; init; }
}

/// <summary>
/// Request to trigger a situational behavior.
/// </summary>
/// <param name="TriggerId">Identifier of the trigger (e.g., "combat_entered").</param>
/// <param name="BehaviorId">The situational behavior to activate.</param>
/// <param name="Priority">Priority within the Situational category.</param>
/// <param name="Duration">Optional duration before automatic deactivation.</param>
public sealed record SituationalTriggerRequest(
    string TriggerId,
    string BehaviorId,
    int Priority = 0,
    TimeSpan? Duration = null);

/// <summary>
/// A single behavior layer in the stack.
/// </summary>
public interface IBehaviorLayer
{
    /// <summary>
    /// Unique identifier for this layer instance.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Human-readable name for debugging/UI.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Category this layer belongs to.
    /// </summary>
    BehaviorCategory Category { get; }

    /// <summary>
    /// Priority within the category (higher = more important).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Whether this layer is currently active and should contribute to outputs.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Evaluates the behavior and produces intent emissions.
    /// </summary>
    /// <param name="context">The evaluation context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Intent emissions from this layer.</returns>
    ValueTask<IReadOnlyList<IntentEmission>> EvaluateAsync(
        BehaviorEvaluationContext context,
        CancellationToken ct);

    /// <summary>
    /// Activates the layer.
    /// </summary>
    void Activate();

    /// <summary>
    /// Deactivates the layer.
    /// </summary>
    void Deactivate();

    /// <summary>
    /// Resets the layer to its initial state.
    /// </summary>
    void Reset();
}

/// <summary>
/// The merged output from evaluating all layers in a behavior stack.
/// </summary>
public sealed class BehaviorStackOutput
{
    /// <summary>
    /// Creates a new stack output.
    /// </summary>
    /// <param name="entityId">The entity this output is for.</param>
    public BehaviorStackOutput(Guid entityId)
    {
        EntityId = entityId;
    }

    /// <summary>
    /// The entity ID this output is for.
    /// </summary>
    public Guid EntityId { get; }

    /// <summary>
    /// The merged emissions per channel.
    /// </summary>
    public Dictionary<string, IntentEmission> MergedEmissions { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// All contributions before merging (for debugging/tracing).
    /// </summary>
    public List<IntentContribution> AllContributions { get; } = new();

    /// <summary>
    /// Which layer won for each channel.
    /// </summary>
    public Dictionary<string, string> WinningLayers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Timestamp when this output was computed.
    /// </summary>
    public DateTime ComputedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the merged emission for a channel.
    /// </summary>
    /// <param name="channel">The channel name.</param>
    /// <returns>The merged emission, or null if no output for this channel.</returns>
    public IntentEmission? GetEmission(string channel)
    {
        return MergedEmissions.GetValueOrDefault(channel);
    }

    /// <summary>
    /// Checks if there's any output for a channel.
    /// </summary>
    /// <param name="channel">The channel name.</param>
    /// <returns>True if an emission exists for this channel.</returns>
    public bool HasEmission(string channel)
    {
        return MergedEmissions.ContainsKey(channel);
    }

    /// <summary>
    /// Gets all channel names with output.
    /// </summary>
    public IReadOnlyCollection<string> ActiveChannels => MergedEmissions.Keys;

    /// <summary>
    /// Creates an empty output.
    /// </summary>
    public static BehaviorStackOutput Empty(Guid entityId) => new(entityId);
}

/// <summary>
/// Manages a stack of behavior layers for an entity.
/// </summary>
public interface IBehaviorStack
{
    /// <summary>
    /// The entity ID this stack manages.
    /// </summary>
    Guid EntityId { get; }

    /// <summary>
    /// The entity's archetype.
    /// </summary>
    IArchetypeDefinition Archetype { get; }

    /// <summary>
    /// All layers in the stack.
    /// </summary>
    IReadOnlyList<IBehaviorLayer> Layers { get; }

    /// <summary>
    /// Active layers only (IsActive = true).
    /// </summary>
    IReadOnlyList<IBehaviorLayer> ActiveLayers { get; }

    /// <summary>
    /// Adds a layer to the stack.
    /// </summary>
    /// <param name="layer">The layer to add.</param>
    void AddLayer(IBehaviorLayer layer);

    /// <summary>
    /// Removes a layer by ID.
    /// </summary>
    /// <param name="layerId">The layer ID to remove.</param>
    /// <returns>True if a layer was removed.</returns>
    bool RemoveLayer(string layerId);

    /// <summary>
    /// Gets a layer by ID.
    /// </summary>
    /// <param name="layerId">The layer ID.</param>
    /// <returns>The layer, or null if not found.</returns>
    IBehaviorLayer? GetLayer(string layerId);

    /// <summary>
    /// Gets all layers in a category.
    /// </summary>
    /// <param name="category">The category to filter by.</param>
    /// <returns>Layers in the category, ordered by priority.</returns>
    IReadOnlyList<IBehaviorLayer> GetLayersByCategory(BehaviorCategory category);

    /// <summary>
    /// Activates a layer by ID.
    /// </summary>
    /// <param name="layerId">The layer ID to activate.</param>
    /// <returns>True if layer was found and activated.</returns>
    bool ActivateLayer(string layerId);

    /// <summary>
    /// Deactivates a layer by ID.
    /// </summary>
    /// <param name="layerId">The layer ID to deactivate.</param>
    /// <returns>True if layer was found and deactivated.</returns>
    bool DeactivateLayer(string layerId);

    /// <summary>
    /// Evaluates all active layers and merges their outputs.
    /// </summary>
    /// <param name="context">The evaluation context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The merged output from all layers.</returns>
    ValueTask<BehaviorStackOutput> EvaluateAsync(
        BehaviorEvaluationContext context,
        CancellationToken ct);

    /// <summary>
    /// Clears all layers from the stack.
    /// </summary>
    void Clear();

    /// <summary>
    /// Event raised when a layer is added.
    /// </summary>
    event EventHandler<BehaviorLayerEventArgs>? LayerAdded;

    /// <summary>
    /// Event raised when a layer is removed.
    /// </summary>
    event EventHandler<BehaviorLayerEventArgs>? LayerRemoved;

    /// <summary>
    /// Event raised when a layer is activated.
    /// </summary>
    event EventHandler<BehaviorLayerEventArgs>? LayerActivated;

    /// <summary>
    /// Event raised when a layer is deactivated.
    /// </summary>
    event EventHandler<BehaviorLayerEventArgs>? LayerDeactivated;
}

/// <summary>
/// Event args for behavior layer events.
/// </summary>
public sealed class BehaviorLayerEventArgs : EventArgs
{
    /// <summary>
    /// Creates new event args.
    /// </summary>
    /// <param name="layer">The layer involved in the event.</param>
    public BehaviorLayerEventArgs(IBehaviorLayer layer)
    {
        Layer = layer ?? throw new ArgumentNullException(nameof(layer));
    }

    /// <summary>
    /// The layer involved in the event.
    /// </summary>
    public IBehaviorLayer Layer { get; }
}

/// <summary>
/// Registry of behavior stacks for entities.
/// </summary>
public interface IBehaviorStackRegistry
{
    /// <summary>
    /// Gets or creates a behavior stack for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="archetype">The entity's archetype.</param>
    /// <returns>The behavior stack for the entity.</returns>
    IBehaviorStack GetOrCreate(Guid entityId, IArchetypeDefinition archetype);

    /// <summary>
    /// Gets an existing behavior stack for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>The behavior stack, or null if not found.</returns>
    IBehaviorStack? Get(Guid entityId);

    /// <summary>
    /// Removes a behavior stack for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>True if a stack was removed.</returns>
    bool Remove(Guid entityId);

    /// <summary>
    /// Gets all entity IDs with registered stacks.
    /// </summary>
    IReadOnlyCollection<Guid> GetEntityIds();

    /// <summary>
    /// Gets the count of registered stacks.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Clears all stacks.
    /// </summary>
    void Clear();
}

/// <summary>
/// Merges intent contributions per channel based on merge strategy.
/// </summary>
public interface IIntentStackMerger
{
    /// <summary>
    /// Merges contributions for a single channel.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="contributions">All contributions for this channel.</param>
    /// <param name="channelDef">The channel definition (for merge strategy).</param>
    /// <returns>The merged emission, or null if no contributions.</returns>
    IntentEmission? MergeChannel(
        string channelName,
        IReadOnlyList<IntentContribution> contributions,
        ILogicalChannelDefinition channelDef);

    /// <summary>
    /// Merges all contributions into per-channel emissions.
    /// </summary>
    /// <param name="contributions">All contributions from all layers.</param>
    /// <param name="archetype">The archetype defining channels and strategies.</param>
    /// <returns>Merged emissions keyed by channel name.</returns>
    Dictionary<string, IntentEmission> MergeAll(
        IReadOnlyList<IntentContribution> contributions,
        IArchetypeDefinition archetype);
}
