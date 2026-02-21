// ═══════════════════════════════════════════════════════════════════════════
// GOAP Preconditions
// Container for action preconditions.
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.Bannou.BehaviorCompiler.Goap;

/// <summary>
/// Container for action preconditions.
/// Maps world state property names to conditions that must be satisfied.
/// </summary>
public sealed class GoapPreconditions
{
    private readonly Dictionary<string, GoapCondition> _conditions;

    /// <summary>
    /// Creates empty preconditions container.
    /// </summary>
    public GoapPreconditions()
    {
        _conditions = new Dictionary<string, GoapCondition>();
    }

    /// <summary>
    /// Creates preconditions from a dictionary of condition strings.
    /// </summary>
    /// <param name="conditionStrings">Map of property names to condition strings.</param>
    /// <returns>Parsed preconditions container.</returns>
    public static GoapPreconditions FromDictionary(IReadOnlyDictionary<string, string> conditionStrings)
    {
        var preconditions = new GoapPreconditions();
        foreach (var (key, value) in conditionStrings)
        {
            preconditions.AddCondition(key, GoapCondition.Parse(value));
        }
        return preconditions;
    }

    /// <summary>
    /// Adds a condition for a property.
    /// </summary>
    /// <param name="propertyName">World state property name.</param>
    /// <param name="condition">Condition that must be satisfied.</param>
    public void AddCondition(string propertyName, GoapCondition condition)
    {
        _conditions[propertyName] = condition;
    }

    /// <summary>
    /// Gets all conditions as key-value pairs.
    /// </summary>
    public IEnumerable<KeyValuePair<string, GoapCondition>> Conditions => _conditions;

    /// <summary>
    /// Gets the number of conditions.
    /// </summary>
    public int Count => _conditions.Count;

    /// <summary>
    /// Checks if there's a condition for a property.
    /// </summary>
    /// <param name="propertyName">Property name to check.</param>
    /// <returns>True if a condition exists for this property.</returns>
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
    /// Gets the property names that have conditions.
    /// </summary>
    public IEnumerable<string> PropertyNames => _conditions.Keys;
}
