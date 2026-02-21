// =============================================================================
// Behavior Layer Base
// Base implementation for behavior layers in the stack.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Archetypes;
using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Behavior.Stack;

/// <summary>
/// Base implementation of a behavior layer.
/// </summary>
/// <remarks>
/// <para>
/// Provides common functionality for all behavior layers:
/// - Activation/deactivation tracking
/// - Context access
/// - Logging
/// </para>
/// <para>
/// Derived classes implement <see cref="EvaluateCoreAsync"/> to produce emissions.
/// </para>
/// </remarks>
public abstract class BehaviorLayerBase : IBehaviorLayer
{
    private readonly ILogger? _logger;
    private bool _isActive;

    /// <summary>
    /// Creates a new behavior layer.
    /// </summary>
    /// <param name="id">Unique layer identifier.</param>
    /// <param name="displayName">Human-readable name.</param>
    /// <param name="category">The behavior category.</param>
    /// <param name="priority">Priority within the category.</param>
    /// <param name="logger">Optional logger.</param>
    protected BehaviorLayerBase(
        string id,
        string displayName,
        BehaviorCategory category,
        int priority,
        ILogger? logger = null)
    {
        Id = id;
        DisplayName = displayName;
        Category = category;
        Priority = priority;
        _logger = logger;
        _isActive = true; // Active by default
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public string DisplayName { get; }

    /// <inheritdoc/>
    public BehaviorCategory Category { get; }

    /// <inheritdoc/>
    public int Priority { get; }

    /// <inheritdoc/>
    public bool IsActive => _isActive;

    /// <summary>
    /// Logger for derived classes.
    /// </summary>
    protected ILogger? Logger => _logger;

    /// <inheritdoc/>
    public void Activate()
    {
        if (_isActive)
        {
            return;
        }

        _isActive = true;
        OnActivated();

        _logger?.LogDebug("Layer {LayerId} activated", Id);
    }

    /// <inheritdoc/>
    public void Deactivate()
    {
        if (!_isActive)
        {
            return;
        }

        _isActive = false;
        OnDeactivated();

        _logger?.LogDebug("Layer {LayerId} deactivated", Id);
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _isActive = true;
        OnReset();

        _logger?.LogDebug("Layer {LayerId} reset", Id);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<IntentEmission>> EvaluateAsync(
        BehaviorEvaluationContext context,
        CancellationToken ct)
    {
        if (!_isActive)
        {
            return Array.Empty<IntentEmission>();
        }

        try
        {
            return await EvaluateCoreAsync(context, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in layer {LayerId} evaluation", Id);
            return Array.Empty<IntentEmission>();
        }
    }

    /// <summary>
    /// Core evaluation logic to be implemented by derived classes.
    /// </summary>
    /// <param name="context">The evaluation context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Intent emissions from this layer.</returns>
    protected abstract ValueTask<IReadOnlyList<IntentEmission>> EvaluateCoreAsync(
        BehaviorEvaluationContext context,
        CancellationToken ct);

    /// <summary>
    /// Called when the layer is activated.
    /// </summary>
    protected virtual void OnActivated()
    {
    }

    /// <summary>
    /// Called when the layer is deactivated.
    /// </summary>
    protected virtual void OnDeactivated()
    {
    }

    /// <summary>
    /// Called when the layer is reset.
    /// </summary>
    protected virtual void OnReset()
    {
    }
}

/// <summary>
/// A simple behavior layer that uses a delegate for evaluation.
/// </summary>
public sealed class DelegateBehaviorLayer : BehaviorLayerBase
{
    private readonly Func<BehaviorEvaluationContext, CancellationToken, ValueTask<IReadOnlyList<IntentEmission>>> _evaluator;

    /// <summary>
    /// Creates a delegate-based behavior layer.
    /// </summary>
    /// <param name="id">Unique layer identifier.</param>
    /// <param name="displayName">Human-readable name.</param>
    /// <param name="category">The behavior category.</param>
    /// <param name="priority">Priority within the category.</param>
    /// <param name="evaluator">The evaluation delegate.</param>
    /// <param name="logger">Optional logger.</param>
    public DelegateBehaviorLayer(
        string id,
        string displayName,
        BehaviorCategory category,
        int priority,
        Func<BehaviorEvaluationContext, CancellationToken, ValueTask<IReadOnlyList<IntentEmission>>> evaluator,
        ILogger? logger = null)
        : base(id, displayName, category, priority, logger)
    {
        _evaluator = evaluator;
    }

    /// <inheritdoc/>
    protected override ValueTask<IReadOnlyList<IntentEmission>> EvaluateCoreAsync(
        BehaviorEvaluationContext context,
        CancellationToken ct)
    {
        return _evaluator(context, ct);
    }

    /// <summary>
    /// Creates a simple layer that always emits the same emissions.
    /// </summary>
    /// <param name="id">Unique layer identifier.</param>
    /// <param name="displayName">Human-readable name.</param>
    /// <param name="category">The behavior category.</param>
    /// <param name="priority">Priority within the category.</param>
    /// <param name="emissions">The emissions to produce.</param>
    /// <returns>A new behavior layer.</returns>
    public static DelegateBehaviorLayer CreateStatic(
        string id,
        string displayName,
        BehaviorCategory category,
        int priority,
        params IntentEmission[] emissions)
    {
        var emissionList = (IReadOnlyList<IntentEmission>)emissions;
        return new DelegateBehaviorLayer(
            id,
            displayName,
            category,
            priority,
            (_, _) => ValueTask.FromResult(emissionList));
    }
}

/// <summary>
/// A behavior layer backed by a compiled behavior model.
/// </summary>
public sealed class ModelBehaviorLayer : BehaviorLayerBase
{
    private readonly Func<BehaviorEvaluationContext, IReadOnlyList<IntentEmission>>? _modelEvaluator;

    /// <summary>
    /// Creates a model-backed behavior layer.
    /// </summary>
    /// <param name="id">Unique layer identifier.</param>
    /// <param name="displayName">Human-readable name.</param>
    /// <param name="category">The behavior category.</param>
    /// <param name="priority">Priority within the category.</param>
    /// <param name="modelEvaluator">Function that evaluates the model and returns emissions.</param>
    /// <param name="logger">Optional logger.</param>
    public ModelBehaviorLayer(
        string id,
        string displayName,
        BehaviorCategory category,
        int priority,
        Func<BehaviorEvaluationContext, IReadOnlyList<IntentEmission>>? modelEvaluator = null,
        ILogger? logger = null)
        : base(id, displayName, category, priority, logger)
    {
        _modelEvaluator = modelEvaluator;
    }

    /// <inheritdoc/>
    protected override ValueTask<IReadOnlyList<IntentEmission>> EvaluateCoreAsync(
        BehaviorEvaluationContext context,
        CancellationToken ct)
    {
        if (_modelEvaluator == null)
        {
            return ValueTask.FromResult<IReadOnlyList<IntentEmission>>(Array.Empty<IntentEmission>());
        }

        var result = _modelEvaluator(context);
        return ValueTask.FromResult(result);
    }
}
