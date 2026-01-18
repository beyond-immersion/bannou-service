using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Actions;

/// <summary>
/// Interface for musical actions that can be planned and executed.
/// Actions represent compositional decisions with measurable effects.
/// </summary>
public interface IMusicalAction
{
    /// <summary>
    /// Unique identifier for this action.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Human-readable name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Category of action (tension, resolution, color, thematic, texture).
    /// </summary>
    ActionCategory Category { get; }

    /// <summary>
    /// Description of what this action does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// The effects this action has on the composition state.
    /// </summary>
    IReadOnlyList<ActionEffect> Effects { get; }

    /// <summary>
    /// Preconditions that must be met to execute this action.
    /// </summary>
    IReadOnlyList<ActionPrecondition> Preconditions { get; }

    /// <summary>
    /// Base cost of this action for GOAP planning.
    /// Lower cost = preferred when multiple actions achieve the same goal.
    /// </summary>
    double BaseCost { get; }

    /// <summary>
    /// Checks if this action can be executed given the current state.
    /// </summary>
    /// <param name="state">The current composition state.</param>
    /// <returns>True if all preconditions are satisfied.</returns>
    bool CanExecute(CompositionState state);

    /// <summary>
    /// Applies this action to the composition state.
    /// </summary>
    /// <param name="state">The state to modify.</param>
    void Apply(CompositionState state);

    /// <summary>
    /// Gets the predicted effects for GOAP planning.
    /// These may differ slightly from actual effects due to context.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <returns>Dictionary of dimension to predicted change.</returns>
    Dictionary<string, double> GetPredictedEffects(CompositionState state);

    /// <summary>
    /// Calculates the actual cost of this action given the current state.
    /// Cost may vary based on context.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <returns>The cost for planning purposes.</returns>
    double CalculateCost(CompositionState state);
}

/// <summary>
/// Categories of musical actions.
/// </summary>
public enum ActionCategory
{
    /// <summary>Actions that increase tension/expectation.</summary>
    Tension,

    /// <summary>Actions that provide resolution/release.</summary>
    Resolution,

    /// <summary>Actions that change harmonic color.</summary>
    Color,

    /// <summary>Actions related to thematic/motivic development.</summary>
    Thematic,

    /// <summary>Actions that change texture/register.</summary>
    Texture,

    /// <summary>Actions related to dynamics and expression.</summary>
    Dynamics
}

/// <summary>
/// Base class for musical actions providing common functionality.
/// </summary>
public abstract class MusicalActionBase : IMusicalAction
{
    /// <inheritdoc/>
    public abstract string Id { get; }

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public abstract ActionCategory Category { get; }

    /// <inheritdoc/>
    public abstract string Description { get; }

    /// <inheritdoc/>
    public abstract IReadOnlyList<ActionEffect> Effects { get; }

    /// <inheritdoc/>
    public virtual IReadOnlyList<ActionPrecondition> Preconditions => [];

    /// <inheritdoc/>
    public virtual double BaseCost => 1.0;

    /// <inheritdoc/>
    public virtual bool CanExecute(CompositionState state)
    {
        foreach (var precondition in Preconditions)
        {
            if (!precondition.IsSatisfied(state))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public virtual void Apply(CompositionState state)
    {
        foreach (var effect in Effects)
        {
            effect.Apply(state);
        }
    }

    /// <inheritdoc/>
    public virtual Dictionary<string, double> GetPredictedEffects(CompositionState state)
    {
        var predictions = new Dictionary<string, double>();

        foreach (var effect in Effects)
        {
            predictions[effect.Dimension] = effect.GetPredictedValue(state);
        }

        return predictions;
    }

    /// <inheritdoc/>
    public virtual double CalculateCost(CompositionState state)
    {
        return BaseCost;
    }

    public override string ToString() => $"{Category}:{Name}";
}
