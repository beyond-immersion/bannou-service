// =============================================================================
// Label Manager
// Manages label allocation and flow offsets during compilation.
// =============================================================================

namespace BeyondImmersion.Bannou.Behavior.Compiler.Codegen;

/// <summary>
/// Manages label allocation and flow offset tracking during compilation.
/// </summary>
public sealed class LabelManager
{
    private int _nextLabelId;
    private readonly Dictionary<string, int> _flowOffsets = new(32, StringComparer.Ordinal);
    private readonly Dictionary<string, int> _flowLabels = new(32, StringComparer.Ordinal);

    /// <summary>
    /// Allocates a new unique label ID.
    /// </summary>
    /// <returns>A unique label identifier.</returns>
    public int AllocateLabel()
    {
        return _nextLabelId++;
    }

    /// <summary>
    /// Allocates multiple labels at once.
    /// </summary>
    /// <param name="count">Number of labels to allocate.</param>
    /// <returns>The first label ID (subsequent IDs are contiguous).</returns>
    public int AllocateLabels(int count)
    {
        var first = _nextLabelId;
        _nextLabelId += count;
        return first;
    }

    /// <summary>
    /// Registers a flow's bytecode offset.
    /// </summary>
    /// <param name="flowName">The flow name.</param>
    /// <param name="bytecodeOffset">The bytecode offset where the flow starts.</param>
    public void RegisterFlowOffset(string flowName, int bytecodeOffset)
    {
        ArgumentNullException.ThrowIfNull(flowName);
        _flowOffsets[flowName] = bytecodeOffset;
    }

    /// <summary>
    /// Gets a flow's bytecode offset.
    /// </summary>
    /// <param name="flowName">The flow name.</param>
    /// <param name="offset">The bytecode offset if found.</param>
    /// <returns>True if the flow was found.</returns>
    public bool TryGetFlowOffset(string flowName, out int offset)
    {
        return _flowOffsets.TryGetValue(flowName, out offset);
    }

    /// <summary>
    /// Gets or allocates a label for a flow.
    /// Used for forward references to flows not yet compiled.
    /// </summary>
    /// <param name="flowName">The flow name.</param>
    /// <returns>The label ID for this flow.</returns>
    public int GetOrAllocateFlowLabel(string flowName)
    {
        ArgumentNullException.ThrowIfNull(flowName);

        if (_flowLabels.TryGetValue(flowName, out var labelId))
        {
            return labelId;
        }

        labelId = AllocateLabel();
        _flowLabels[flowName] = labelId;
        return labelId;
    }

    /// <summary>
    /// Gets all registered flow names.
    /// </summary>
    /// <returns>Enumerable of flow names.</returns>
    public IEnumerable<string> GetFlowNames()
    {
        return _flowOffsets.Keys;
    }

    /// <summary>
    /// Gets all flow labels for patching.
    /// </summary>
    /// <returns>Dictionary mapping flow names to label IDs.</returns>
    public IReadOnlyDictionary<string, int> GetFlowLabels()
    {
        return _flowLabels;
    }

    /// <summary>
    /// Resets the label manager to initial state.
    /// </summary>
    public void Reset()
    {
        _nextLabelId = 0;
        _flowOffsets.Clear();
        _flowLabels.Clear();
    }
}
