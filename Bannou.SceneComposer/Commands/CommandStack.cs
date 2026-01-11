namespace BeyondImmersion.Bannou.SceneComposer.Commands;

/// <summary>
/// Manages the undo/redo history for editor commands.
/// Supports command merging for continuous operations and compound commands.
/// </summary>
public class CommandStack
{
    private readonly List<IEditorCommand> _undoStack = new();
    private readonly List<IEditorCommand> _redoStack = new();
    private readonly int _maxDepth;
    private CompoundCommand? _activeCompound;
    private int _compoundDepth;
    private DateTime _lastCommandTime = DateTime.MinValue;
    private readonly TimeSpan _mergeWindow = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Raised when the undo/redo state changes.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Raised when a command is executed.
    /// </summary>
    public event EventHandler<CommandExecutedEventArgs>? CommandExecuted;

    /// <summary>
    /// Maximum number of commands to keep in history.
    /// </summary>
    public int MaxDepth => _maxDepth;

    /// <summary>
    /// Current number of undoable commands.
    /// </summary>
    public int UndoCount => _undoStack.Count;

    /// <summary>
    /// Current number of redoable commands.
    /// </summary>
    public int RedoCount => _redoStack.Count;

    /// <summary>
    /// Whether an undo operation is available.
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0 && _activeCompound == null;

    /// <summary>
    /// Whether a redo operation is available.
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0 && _activeCompound == null;

    /// <summary>
    /// Description of the next undo operation.
    /// </summary>
    public string? UndoDescription => _undoStack.Count > 0 ? _undoStack[^1].Description : null;

    /// <summary>
    /// Description of the next redo operation.
    /// </summary>
    public string? RedoDescription => _redoStack.Count > 0 ? _redoStack[^1].Description : null;

    /// <summary>
    /// Whether a compound operation is in progress.
    /// </summary>
    public bool IsInCompound => _activeCompound != null;

    /// <summary>
    /// Create a command stack.
    /// </summary>
    /// <param name="maxDepth">Maximum undo depth (default 100).</param>
    public CommandStack(int maxDepth = 100)
    {
        if (maxDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "Max depth must be at least 1.");
        _maxDepth = maxDepth;
    }

    /// <summary>
    /// Execute a command and add it to the undo stack.
    /// </summary>
    /// <param name="command">Command to execute.</param>
    /// <param name="allowMerge">Whether to allow merging with the previous command.</param>
    public void Execute(IEditorCommand command, bool allowMerge = true)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));

        // Execute the command
        command.Execute();

        // If we're in a compound, add to compound instead of stack
        if (_activeCompound != null)
        {
            _activeCompound.Add(command);
            CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, CommandExecutionType.Execute));
            return;
        }

        // Try to merge with previous command if within time window
        var now = DateTime.UtcNow;
        if (allowMerge && _undoStack.Count > 0 && (now - _lastCommandTime) < _mergeWindow)
        {
            var last = _undoStack[^1];
            if (last.CanMergeWith(command) && last.TryMerge(command))
            {
                _lastCommandTime = now;
                CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, CommandExecutionType.Merged));
                StateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        // Clear redo stack on new command
        _redoStack.Clear();

        // Add to undo stack
        _undoStack.Add(command);
        _lastCommandTime = now;

        // Trim if over capacity
        while (_undoStack.Count > _maxDepth)
        {
            _undoStack.RemoveAt(0);
        }

        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, CommandExecutionType.Execute));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Undo the last command.
    /// </summary>
    /// <returns>True if undo was performed.</returns>
    public bool Undo()
    {
        if (!CanUndo) return false;

        var command = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        command.Undo();

        _redoStack.Add(command);

        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, CommandExecutionType.Undo));
        StateChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>
    /// Redo the last undone command.
    /// </summary>
    /// <returns>True if redo was performed.</returns>
    public bool Redo()
    {
        if (!CanRedo) return false;

        var command = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);

        command.Execute();

        _undoStack.Add(command);

        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, CommandExecutionType.Redo));
        StateChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>
    /// Begin a compound operation.
    /// All commands until EndCompound() will be grouped as one undo operation.
    /// Can be nested.
    /// </summary>
    /// <param name="description">Description of the compound operation.</param>
    /// <returns>Disposable that ends the compound when disposed.</returns>
    public IDisposable BeginCompound(string description)
    {
        if (description == null) throw new ArgumentNullException(nameof(description));

        _compoundDepth++;

        if (_activeCompound == null)
        {
            _activeCompound = new CompoundCommand(description);
        }

        return new CompoundScope(this);
    }

    /// <summary>
    /// End the current compound operation.
    /// </summary>
    private void EndCompound()
    {
        if (_compoundDepth == 0) return;

        _compoundDepth--;

        if (_compoundDepth == 0 && _activeCompound != null)
        {
            // Only add compound if it has commands
            if (_activeCompound.Commands.Count > 0)
            {
                _redoStack.Clear();
                _undoStack.Add(_activeCompound);
                _lastCommandTime = DateTime.UtcNow;

                while (_undoStack.Count > _maxDepth)
                {
                    _undoStack.RemoveAt(0);
                }

                StateChanged?.Invoke(this, EventArgs.Empty);
            }

            _activeCompound = null;
        }
    }

    /// <summary>
    /// Clear all undo/redo history.
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _activeCompound = null;
        _compoundDepth = 0;
        _lastCommandTime = DateTime.MinValue;

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Get the undo history descriptions (most recent first).
    /// </summary>
    public IEnumerable<string> GetUndoHistory()
    {
        for (int i = _undoStack.Count - 1; i >= 0; i--)
        {
            yield return _undoStack[i].Description;
        }
    }

    /// <summary>
    /// Get the redo history descriptions (most recent first).
    /// </summary>
    public IEnumerable<string> GetRedoHistory()
    {
        for (int i = _redoStack.Count - 1; i >= 0; i--)
        {
            yield return _redoStack[i].Description;
        }
    }

    /// <summary>
    /// Set the time window for command merging.
    /// </summary>
    /// <param name="window">Time window (default 500ms).</param>
    public void SetMergeWindow(TimeSpan window)
    {
        if (window < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window), "Window must be non-negative.");
    }

    /// <summary>
    /// Force a break in command merging.
    /// The next command will not merge with the previous one.
    /// </summary>
    public void BreakMerge()
    {
        _lastCommandTime = DateTime.MinValue;
    }

    /// <summary>
    /// Disposable scope for compound operations.
    /// </summary>
    private class CompoundScope : IDisposable
    {
        private readonly CommandStack _stack;
        private bool _disposed;

        public CompoundScope(CommandStack stack)
        {
            _stack = stack;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stack.EndCompound();
        }
    }
}

/// <summary>
/// Event args for command execution.
/// </summary>
public class CommandExecutedEventArgs : EventArgs
{
    /// <summary>
    /// The command that was executed.
    /// </summary>
    public IEditorCommand Command { get; }

    /// <summary>
    /// How the command was executed.
    /// </summary>
    public CommandExecutionType ExecutionType { get; }

    public CommandExecutedEventArgs(IEditorCommand command, CommandExecutionType executionType)
    {
        Command = command;
        ExecutionType = executionType;
    }
}

/// <summary>
/// How a command was executed.
/// </summary>
public enum CommandExecutionType
{
    /// <summary>Normal execution.</summary>
    Execute,
    /// <summary>Merged with previous command.</summary>
    Merged,
    /// <summary>Undone.</summary>
    Undo,
    /// <summary>Redone.</summary>
    Redo
}
