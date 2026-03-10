// ═══════════════════════════════════════════════════════════════════════════
// GOAP Action
// Represents a GOAP action with preconditions, effects, and cost.
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.Bannou.Server.Behavior.Goap;

/// <summary>
/// Represents a GOAP action that can be planned.
/// Each action has preconditions that must be satisfied, effects it produces, and a cost.
/// </summary>
public sealed class GoapAction
{
    /// <summary>
    /// Unique identifier for this action (typically the flow name).
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Human-readable name for this action.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Preconditions that must be satisfied to execute this action.
    /// </summary>
    public GoapPreconditions Preconditions { get; }

    /// <summary>
    /// Effects applied to world state when action completes.
    /// </summary>
    public GoapActionEffects Effects { get; }

    /// <summary>
    /// Cost of executing this action (lower = preferred in plans).
    /// </summary>
    public float Cost { get; }

    /// <summary>
    /// Creates a new GOAP action.
    /// </summary>
    /// <param name="id">Action identifier.</param>
    /// <param name="name">Action name.</param>
    /// <param name="preconditions">Preconditions.</param>
    /// <param name="effects">Effects.</param>
    /// <param name="cost">Action cost.</param>
    public GoapAction(
        string id,
        string name,
        GoapPreconditions preconditions,
        GoapActionEffects effects,
        float cost = 1.0f)
    {

        if (cost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cost), "Cost cannot be negative");
        }

        Id = id;
        Name = name;
        Preconditions = preconditions;
        Effects = effects;
        Cost = cost;
    }

    /// <summary>
    /// Creates a GOAP action from parsed metadata.
    /// </summary>
    /// <param name="flowName">The flow name as action ID and name.</param>
    /// <param name="preconditionStrings">Map of property names to condition strings.</param>
    /// <param name="effectStrings">Map of property names to effect strings.</param>
    /// <param name="cost">Action cost.</param>
    /// <returns>New GOAP action.</returns>
    public static GoapAction FromMetadata(
        string flowName,
        IReadOnlyDictionary<string, string> preconditionStrings,
        IReadOnlyDictionary<string, string> effectStrings,
        float cost = 1.0f)
    {
        var preconditions = GoapPreconditions.FromDictionary(preconditionStrings);
        var effects = GoapActionEffects.FromDictionary(effectStrings);
        return new GoapAction(flowName, flowName, preconditions, effects, cost);
    }

    /// <summary>
    /// Checks if this action can be executed given the current world state.
    /// </summary>
    /// <param name="state">Current world state.</param>
    /// <returns>True if all preconditions are satisfied.</returns>
    public bool IsApplicable(WorldState state)
    {
        return state.SatisfiesPreconditions(Preconditions);
    }

    /// <summary>
    /// Applies this action's effects to a world state.
    /// </summary>
    /// <param name="state">Current world state.</param>
    /// <returns>New world state with effects applied.</returns>
    public WorldState Apply(WorldState state)
    {
        return state.ApplyEffects(Effects);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"GoapAction({Id}, cost={Cost})";
    }
}
