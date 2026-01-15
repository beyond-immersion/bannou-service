// ═══════════════════════════════════════════════════════════════════════════
// GOAP Goal
// Represents a goal with priority and satisfaction conditions.
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.Bannou.Behavior.Goap;

/// <summary>
/// Represents a GOAP goal with priority and conditions.
/// Goals define desired world state conditions that the planner tries to achieve.
/// </summary>
public sealed class GoapGoal
{
    private readonly Dictionary<string, GoapCondition> _conditions;

    /// <summary>
    /// Unique identifier for this goal.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Human-readable name for this goal.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Goal priority (higher = more important).
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Conditions that define goal satisfaction.
    /// </summary>
    public IEnumerable<KeyValuePair<string, GoapCondition>> Conditions => _conditions;

    /// <summary>
    /// Creates a new GOAP goal.
    /// </summary>
    /// <param name="id">Goal identifier.</param>
    /// <param name="name">Goal name.</param>
    /// <param name="priority">Goal priority.</param>
    public GoapGoal(string id, string name, int priority = 50)
    {

        Id = id;
        Name = name;
        Priority = priority;
        _conditions = new Dictionary<string, GoapCondition>();
    }

    /// <summary>
    /// Creates a GOAP goal from parsed metadata.
    /// </summary>
    /// <param name="goalName">The goal name as ID and name.</param>
    /// <param name="priority">Goal priority.</param>
    /// <param name="conditionStrings">Map of property names to condition strings.</param>
    /// <returns>New GOAP goal.</returns>
    public static GoapGoal FromMetadata(
        string goalName,
        int priority,
        IReadOnlyDictionary<string, string> conditionStrings)
    {
        var goal = new GoapGoal(goalName, goalName, priority);
        foreach (var (key, value) in conditionStrings)
        {
            goal.AddCondition(key, GoapCondition.Parse(value));
        }
        return goal;
    }

    /// <summary>
    /// Adds a condition to this goal.
    /// </summary>
    /// <param name="propertyName">World state property name.</param>
    /// <param name="condition">Condition that must be satisfied.</param>
    public void AddCondition(string propertyName, GoapCondition condition)
    {
        _conditions[propertyName] = condition;
    }

    /// <summary>
    /// Checks if the goal has a condition for a property.
    /// </summary>
    /// <param name="propertyName">Property name to check.</param>
    /// <returns>True if a condition exists.</returns>
    public bool HasCondition(string propertyName) => _conditions.ContainsKey(propertyName);

    /// <summary>
    /// Gets the condition for a property.
    /// </summary>
    /// <param name="propertyName">Property name.</param>
    /// <returns>The condition, or null if not found.</returns>
    public GoapCondition? GetCondition(string propertyName)
    {
        return _conditions.TryGetValue(propertyName, out var condition) ? condition : null;
    }

    /// <summary>
    /// Gets the number of conditions in this goal.
    /// </summary>
    public int ConditionCount => _conditions.Count;

    /// <summary>
    /// Gets the property names that have conditions.
    /// </summary>
    public IEnumerable<string> PropertyNames => _conditions.Keys;

    /// <summary>
    /// Checks if this goal is satisfied by a world state.
    /// </summary>
    /// <param name="state">World state to check.</param>
    /// <returns>True if all conditions are satisfied.</returns>
    public bool IsSatisfiedBy(WorldState state)
    {
        return state.SatisfiesGoal(this);
    }

    /// <summary>
    /// Calculates the heuristic distance from a state to this goal.
    /// </summary>
    /// <param name="state">Current world state.</param>
    /// <returns>Estimated distance (0 if satisfied).</returns>
    public float DistanceFrom(WorldState state)
    {
        return state.DistanceToGoal(this);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"GoapGoal({Id}, priority={Priority}, conditions={_conditions.Count})";
    }
}
