// ═══════════════════════════════════════════════════════════════════════════
// GOAP Metadata Converter
// Converts parsed GOAP metadata to runtime planning types.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents;

namespace BeyondImmersion.Bannou.Behavior.Goap;

/// <summary>
/// Converts parsed GOAP metadata from ABML documents to runtime planning types.
/// </summary>
public static class GoapMetadataConverter
{
    /// <summary>
    /// Converts a parsed goal definition to a GOAP goal.
    /// </summary>
    /// <param name="name">Goal name.</param>
    /// <param name="definition">Parsed goal definition.</param>
    /// <returns>GOAP goal ready for planning.</returns>
    public static GoapGoal ToGoapGoal(string name, GoapGoalDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(definition);

        return GoapGoal.FromMetadata(name, definition.Priority, definition.Conditions);
    }

    /// <summary>
    /// Converts parsed flow GOAP metadata to a GOAP action.
    /// </summary>
    /// <param name="flowName">Flow name (becomes action ID).</param>
    /// <param name="metadata">Parsed GOAP metadata.</param>
    /// <returns>GOAP action ready for planning.</returns>
    public static GoapAction ToGoapAction(string flowName, GoapFlowMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(flowName);
        ArgumentNullException.ThrowIfNull(metadata);

        return GoapAction.FromMetadata(
            flowName,
            metadata.Preconditions,
            metadata.Effects,
            metadata.Cost);
    }

    /// <summary>
    /// Extracts all goals from an ABML document.
    /// </summary>
    /// <param name="document">Parsed ABML document.</param>
    /// <returns>List of GOAP goals.</returns>
    public static IReadOnlyList<GoapGoal> ExtractGoals(AbmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var goals = new List<GoapGoal>();
        foreach (var (name, definition) in document.Goals)
        {
            goals.Add(ToGoapGoal(name, definition));
        }
        return goals;
    }

    /// <summary>
    /// Extracts all GOAP-enabled actions from an ABML document.
    /// Only flows with goap: blocks are included.
    /// </summary>
    /// <param name="document">Parsed ABML document.</param>
    /// <returns>List of GOAP actions.</returns>
    public static IReadOnlyList<GoapAction> ExtractActions(AbmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var actions = new List<GoapAction>();
        foreach (var (flowName, flow) in document.Flows)
        {
            if (flow.Goap != null)
            {
                actions.Add(ToGoapAction(flowName, flow.Goap));
            }
        }
        return actions;
    }

    /// <summary>
    /// Extracts both goals and actions from an ABML document.
    /// </summary>
    /// <param name="document">Parsed ABML document.</param>
    /// <returns>Tuple of goals and actions.</returns>
    public static (IReadOnlyList<GoapGoal> Goals, IReadOnlyList<GoapAction> Actions) ExtractAll(
        AbmlDocument document)
    {
        return (ExtractGoals(document), ExtractActions(document));
    }

    /// <summary>
    /// Checks if a document has any GOAP content (goals or GOAP-enabled flows).
    /// </summary>
    /// <param name="document">Parsed ABML document.</param>
    /// <returns>True if document has GOAP content.</returns>
    public static bool HasGoapContent(AbmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Goals.Count > 0)
        {
            return true;
        }

        foreach (var (_, flow) in document.Flows)
        {
            if (flow.Goap != null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the count of GOAP goals in a document.
    /// </summary>
    /// <param name="document">Parsed ABML document.</param>
    /// <returns>Number of goals.</returns>
    public static int GetGoalCount(AbmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Goals.Count;
    }

    /// <summary>
    /// Gets the count of GOAP-enabled actions in a document.
    /// </summary>
    /// <param name="document">Parsed ABML document.</param>
    /// <returns>Number of actions with goap: blocks.</returns>
    public static int GetActionCount(AbmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var count = 0;
        foreach (var (_, flow) in document.Flows)
        {
            if (flow.Goap != null)
            {
                count++;
            }
        }
        return count;
    }
}
