namespace BeyondImmersion.Bannou.SceneComposer.Commands;

/// <summary>
/// Command interface for undoable editor operations.
/// All scene modifications should be wrapped in commands.
/// </summary>
public interface IEditorCommand
{
    /// <summary>
    /// Human-readable description of this command.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Execute the command (do the operation).
    /// </summary>
    void Execute();

    /// <summary>
    /// Undo the command (reverse the operation).
    /// </summary>
    void Undo();

    /// <summary>
    /// Whether this command can be merged with another command of the same type.
    /// Used for continuous operations like dragging.
    /// </summary>
    bool CanMergeWith(IEditorCommand other);

    /// <summary>
    /// Merge this command with another command.
    /// Returns true if merge was successful.
    /// </summary>
    bool TryMerge(IEditorCommand other);
}

/// <summary>
/// Command that groups multiple commands into a single undo operation.
/// </summary>
public class CompoundCommand : IEditorCommand
{
    private readonly List<IEditorCommand> _commands = new();

    /// <summary>
    /// Description of the compound operation.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Child commands in this compound.
    /// </summary>
    public IReadOnlyList<IEditorCommand> Commands => _commands;

    /// <summary>
    /// Create a compound command.
    /// </summary>
    /// <param name="description">Description of the compound operation.</param>
    public CompoundCommand(string description)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    /// <summary>
    /// Add a command to this compound.
    /// </summary>
    public void Add(IEditorCommand command)
    {
        _commands.Add(command ?? throw new ArgumentNullException(nameof(command)));
    }

    /// <summary>
    /// Execute all child commands in order.
    /// </summary>
    public void Execute()
    {
        foreach (var command in _commands)
        {
            command.Execute();
        }
    }

    /// <summary>
    /// Undo all child commands in reverse order.
    /// </summary>
    public void Undo()
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
        {
            _commands[i].Undo();
        }
    }

    /// <summary>
    /// Compound commands cannot be merged.
    /// </summary>
    public bool CanMergeWith(IEditorCommand other) => false;

    /// <summary>
    /// Compound commands cannot be merged.
    /// </summary>
    public bool TryMerge(IEditorCommand other) => false;
}

/// <summary>
/// Base class for commands that modify a single node's property.
/// </summary>
/// <typeparam name="T">Type of the property value.</typeparam>
public abstract class NodePropertyCommand<T> : IEditorCommand
{
    /// <summary>
    /// ID of the node being modified.
    /// </summary>
    protected Guid NodeId { get; }

    /// <summary>
    /// Value before the change.
    /// </summary>
    protected T OldValue { get; }

    /// <summary>
    /// Value after the change.
    /// </summary>
    protected T NewValue { get; set; }

    /// <summary>
    /// Description of this command.
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// Create a node property command.
    /// </summary>
    protected NodePropertyCommand(Guid nodeId, T oldValue, T newValue)
    {
        NodeId = nodeId;
        OldValue = oldValue;
        NewValue = newValue;
    }

    /// <summary>
    /// Apply the new value.
    /// </summary>
    public abstract void Execute();

    /// <summary>
    /// Apply the old value.
    /// </summary>
    public abstract void Undo();

    /// <summary>
    /// Check if this command can merge with another.
    /// Default: can merge if same type and same node.
    /// </summary>
    public virtual bool CanMergeWith(IEditorCommand other)
    {
        return other is NodePropertyCommand<T> otherCmd && otherCmd.NodeId == NodeId;
    }

    /// <summary>
    /// Merge with another command by taking its new value.
    /// </summary>
    public virtual bool TryMerge(IEditorCommand other)
    {
        if (!CanMergeWith(other)) return false;
        var otherCmd = (NodePropertyCommand<T>)other;
        NewValue = otherCmd.NewValue;
        return true;
    }
}
