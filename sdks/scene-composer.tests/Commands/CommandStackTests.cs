using BeyondImmersion.Bannou.SceneComposer.Commands;
using Xunit;

namespace BeyondImmersion.Bannou.SceneComposer.Tests.Commands;

/// <summary>
/// Tests for the CommandStack class.
/// </summary>
public class CommandStackTests
{
    // =========================================================================
    // CONSTRUCTION
    // =========================================================================

    [Fact]
    public void Constructor_DefaultMaxDepth()
    {
        var stack = new CommandStack();

        Assert.Equal(100, stack.MaxDepth);
    }

    [Fact]
    public void Constructor_CustomMaxDepth()
    {
        var stack = new CommandStack(50);

        Assert.Equal(50, stack.MaxDepth);
    }

    [Fact]
    public void Constructor_ThrowsOnInvalidMaxDepth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommandStack(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommandStack(-1));
    }

    [Fact]
    public void InitialState_EmptyStacks()
    {
        var stack = new CommandStack();

        Assert.Equal(0, stack.UndoCount);
        Assert.Equal(0, stack.RedoCount);
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Null(stack.UndoDescription);
        Assert.Null(stack.RedoDescription);
        Assert.False(stack.IsInCompound);
    }

    // =========================================================================
    // EXECUTE
    // =========================================================================

    [Fact]
    public void Execute_AddsToUndoStack()
    {
        var stack = new CommandStack();
        var command = new TestCommand("Test");

        stack.Execute(command);

        Assert.Equal(1, stack.UndoCount);
        Assert.True(stack.CanUndo);
        Assert.Equal("Test", stack.UndoDescription);
        Assert.True(command.WasExecuted);
    }

    [Fact]
    public void Execute_ClearsRedoStack()
    {
        var stack = new CommandStack();
        stack.Execute(new TestCommand("First"));
        stack.Undo();
        Assert.True(stack.CanRedo);

        stack.Execute(new TestCommand("Second"));

        Assert.False(stack.CanRedo);
        Assert.Equal(0, stack.RedoCount);
    }

    [Fact]
    public void Execute_TrimsToMaxDepth()
    {
        var stack = new CommandStack(3);

        stack.Execute(new TestCommand("1"));
        stack.Execute(new TestCommand("2"));
        stack.Execute(new TestCommand("3"));
        stack.Execute(new TestCommand("4"));

        Assert.Equal(3, stack.UndoCount);
        // The first command should have been removed
        Assert.Equal("4", stack.UndoDescription);
    }

    [Fact]
    public void Execute_RaisesStateChangedEvent()
    {
        var stack = new CommandStack();
        var eventRaised = false;
        stack.StateChanged += (_, _) => eventRaised = true;

        stack.Execute(new TestCommand("Test"));

        Assert.True(eventRaised);
    }

    [Fact]
    public void Execute_RaisesCommandExecutedEvent()
    {
        var stack = new CommandStack();
        CommandExecutedEventArgs? args = null;
        stack.CommandExecuted += (_, e) => args = e;

        var command = new TestCommand("Test");
        stack.Execute(command);

        Assert.NotNull(args);
        Assert.Same(command, args.Command);
        Assert.Equal(CommandExecutionType.Execute, args.ExecutionType);
    }

    // =========================================================================
    // UNDO
    // =========================================================================

    [Fact]
    public void Undo_UndoesCommand()
    {
        var stack = new CommandStack();
        var command = new TestCommand("Test");
        stack.Execute(command);

        var result = stack.Undo();

        Assert.True(result);
        Assert.True(command.WasUndone);
    }

    [Fact]
    public void Undo_MovesToRedoStack()
    {
        var stack = new CommandStack();
        stack.Execute(new TestCommand("Test"));

        stack.Undo();

        Assert.Equal(0, stack.UndoCount);
        Assert.Equal(1, stack.RedoCount);
        Assert.True(stack.CanRedo);
        Assert.Equal("Test", stack.RedoDescription);
    }

    [Fact]
    public void Undo_ReturnsFalseWhenEmpty()
    {
        var stack = new CommandStack();

        var result = stack.Undo();

        Assert.False(result);
    }

    [Fact]
    public void Undo_RaisesEvents()
    {
        var stack = new CommandStack();
        stack.Execute(new TestCommand("Test"));
        CommandExecutedEventArgs? args = null;
        var stateChanged = false;
        stack.CommandExecuted += (_, e) => args = e;
        stack.StateChanged += (_, _) => stateChanged = true;

        stack.Undo();

        Assert.NotNull(args);
        Assert.Equal(CommandExecutionType.Undo, args.ExecutionType);
        Assert.True(stateChanged);
    }

    // =========================================================================
    // REDO
    // =========================================================================

    [Fact]
    public void Redo_RedoesCommand()
    {
        var stack = new CommandStack();
        var command = new TestCommand("Test");
        stack.Execute(command);
        stack.Undo();
        command.Reset();

        var result = stack.Redo();

        Assert.True(result);
        Assert.True(command.WasExecuted);
    }

    [Fact]
    public void Redo_MovesToUndoStack()
    {
        var stack = new CommandStack();
        stack.Execute(new TestCommand("Test"));
        stack.Undo();

        stack.Redo();

        Assert.Equal(1, stack.UndoCount);
        Assert.Equal(0, stack.RedoCount);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Redo_ReturnsFalseWhenEmpty()
    {
        var stack = new CommandStack();

        var result = stack.Redo();

        Assert.False(result);
    }

    [Fact]
    public void Redo_RaisesEvents()
    {
        var stack = new CommandStack();
        stack.Execute(new TestCommand("Test"));
        stack.Undo();
        CommandExecutedEventArgs? args = null;
        var stateChanged = false;
        stack.CommandExecuted += (_, e) => args = e;
        stack.StateChanged += (_, _) => stateChanged = true;

        stack.Redo();

        Assert.NotNull(args);
        Assert.Equal(CommandExecutionType.Redo, args.ExecutionType);
        Assert.True(stateChanged);
    }

    // =========================================================================
    // MULTIPLE UNDO/REDO
    // =========================================================================

    [Fact]
    public void UndoRedo_Sequence()
    {
        var stack = new CommandStack();
        var cmd1 = new TestCommand("1");
        var cmd2 = new TestCommand("2");
        var cmd3 = new TestCommand("3");

        stack.Execute(cmd1);
        stack.Execute(cmd2);
        stack.Execute(cmd3);

        Assert.Equal(3, stack.UndoCount);

        stack.Undo(); // Undo cmd3
        Assert.Equal("2", stack.UndoDescription);

        stack.Undo(); // Undo cmd2
        Assert.Equal("1", stack.UndoDescription);

        stack.Redo(); // Redo cmd2
        Assert.Equal("2", stack.UndoDescription);
        Assert.Equal("3", stack.RedoDescription);
    }

    // =========================================================================
    // COMPOUND COMMANDS
    // =========================================================================

    [Fact]
    public void BeginCompound_SetsCompoundFlag()
    {
        var stack = new CommandStack();

        using (stack.BeginCompound("Test Compound"))
        {
            Assert.True(stack.IsInCompound);
        }

        Assert.False(stack.IsInCompound);
    }

    [Fact]
    public void Compound_GroupsCommandsAsOne()
    {
        var stack = new CommandStack();

        using (stack.BeginCompound("Test Compound"))
        {
            stack.Execute(new TestCommand("1"));
            stack.Execute(new TestCommand("2"));
            stack.Execute(new TestCommand("3"));
        }

        Assert.Equal(1, stack.UndoCount);
        Assert.Equal("Test Compound", stack.UndoDescription);
    }

    [Fact]
    public void Compound_UndoesAllAtOnce()
    {
        var stack = new CommandStack();
        var cmd1 = new TestCommand("1");
        var cmd2 = new TestCommand("2");
        var cmd3 = new TestCommand("3");

        using (stack.BeginCompound("Test Compound"))
        {
            stack.Execute(cmd1);
            stack.Execute(cmd2);
            stack.Execute(cmd3);
        }

        stack.Undo();

        Assert.True(cmd1.WasUndone);
        Assert.True(cmd2.WasUndone);
        Assert.True(cmd3.WasUndone);
    }

    [Fact]
    public void Compound_CanBeNested()
    {
        var stack = new CommandStack();
        var cmd1 = new TestCommand("1");
        var cmd2 = new TestCommand("2");
        var cmd3 = new TestCommand("3");

        using (stack.BeginCompound("Outer"))
        {
            stack.Execute(cmd1);
            using (stack.BeginCompound("Inner"))
            {
                stack.Execute(cmd2);
            }
            stack.Execute(cmd3);
        }

        Assert.Equal(1, stack.UndoCount);
        Assert.Equal("Outer", stack.UndoDescription);

        stack.Undo();
        Assert.True(cmd1.WasUndone);
        Assert.True(cmd2.WasUndone);
        Assert.True(cmd3.WasUndone);
    }

    [Fact]
    public void Compound_EmptyCompoundNotAdded()
    {
        var stack = new CommandStack();

        using (stack.BeginCompound("Empty"))
        {
            // No commands executed
        }

        Assert.Equal(0, stack.UndoCount);
    }

    [Fact]
    public void CannotUndoWhileInCompound()
    {
        var stack = new CommandStack();
        stack.Execute(new TestCommand("Before"));

        using (stack.BeginCompound("Test"))
        {
            stack.Execute(new TestCommand("During"));
            Assert.False(stack.CanUndo); // Cannot undo while compound is active
        }

        Assert.True(stack.CanUndo); // Can undo after compound ends
    }

    // =========================================================================
    // CLEAR
    // =========================================================================

    [Fact]
    public void Clear_RemovesAllHistory()
    {
        var stack = new CommandStack();
        stack.Execute(new TestCommand("1"));
        stack.Execute(new TestCommand("2"));
        stack.Undo();

        stack.Clear();

        Assert.Equal(0, stack.UndoCount);
        Assert.Equal(0, stack.RedoCount);
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Clear_EndsPendingCompound()
    {
        var stack = new CommandStack();
        stack.BeginCompound("Test"); // Note: not using 'using'

        stack.Clear();

        Assert.False(stack.IsInCompound);
    }

    [Fact]
    public void Clear_RaisesStateChanged()
    {
        var stack = new CommandStack();
        stack.Execute(new TestCommand("Test"));
        var eventRaised = false;
        stack.StateChanged += (_, _) => eventRaised = true;

        stack.Clear();

        Assert.True(eventRaised);
    }

    // =========================================================================
    // HISTORY
    // =========================================================================

    [Fact]
    public void GetUndoHistory_ReturnsMostRecentFirst()
    {
        var stack = new CommandStack();
        stack.Execute(new TestCommand("First"));
        stack.Execute(new TestCommand("Second"));
        stack.Execute(new TestCommand("Third"));

        var history = stack.GetUndoHistory().ToList();

        Assert.Equal(3, history.Count);
        Assert.Equal("Third", history[0]);
        Assert.Equal("Second", history[1]);
        Assert.Equal("First", history[2]);
    }

    [Fact]
    public void GetRedoHistory_ReturnsMostRecentFirst()
    {
        var stack = new CommandStack();
        stack.Execute(new TestCommand("First"));
        stack.Execute(new TestCommand("Second"));
        stack.Execute(new TestCommand("Third"));
        stack.Undo();  // Undo Third -> redo: [Third]
        stack.Undo();  // Undo Second -> redo: [Third, Second]

        var history = stack.GetRedoHistory().ToList();

        // GetRedoHistory iterates from end (most recently undone) to start
        Assert.Equal(2, history.Count);
        Assert.Equal("Second", history[0]);  // Second was undone last
        Assert.Equal("Third", history[1]);   // Third was undone first
    }

    // =========================================================================
    // COMMAND MERGING
    // =========================================================================

    [Fact]
    public void Execute_MergesCompatibleCommands()
    {
        var stack = new CommandStack();
        var nodeId = Guid.NewGuid();
        var cmd1 = new MergeableTestCommand(nodeId, 1);
        var cmd2 = new MergeableTestCommand(nodeId, 2);

        stack.Execute(cmd1);
        stack.Execute(cmd2); // Should merge

        Assert.Equal(1, stack.UndoCount);
        Assert.Equal(2, cmd1.Value); // Value should be updated from merge
    }

    [Fact]
    public void Execute_DoesNotMergeIncompatibleCommands()
    {
        var stack = new CommandStack();
        var cmd1 = new MergeableTestCommand(Guid.NewGuid(), 1);
        var cmd2 = new MergeableTestCommand(Guid.NewGuid(), 2); // Different node

        stack.Execute(cmd1);
        stack.Execute(cmd2);

        Assert.Equal(2, stack.UndoCount);
    }

    [Fact]
    public void Execute_DoesNotMergeAfterMergeWindow()
    {
        // Note: This test relies on implementation details.
        // The merge window is 500ms by default.
        var stack = new CommandStack();
        var nodeId = Guid.NewGuid();

        stack.Execute(new MergeableTestCommand(nodeId, 1));
        stack.BreakMerge(); // Force break
        stack.Execute(new MergeableTestCommand(nodeId, 2));

        Assert.Equal(2, stack.UndoCount);
    }

    [Fact]
    public void Execute_AllowMergeFalse_PreventsMerge()
    {
        var stack = new CommandStack();
        var nodeId = Guid.NewGuid();

        stack.Execute(new MergeableTestCommand(nodeId, 1));
        stack.Execute(new MergeableTestCommand(nodeId, 2), allowMerge: false);

        Assert.Equal(2, stack.UndoCount);
    }

    [Fact]
    public void Execute_MergeRaisesCorrectEventType()
    {
        var stack = new CommandStack();
        var nodeId = Guid.NewGuid();
        stack.Execute(new MergeableTestCommand(nodeId, 1));

        CommandExecutedEventArgs? args = null;
        stack.CommandExecuted += (_, e) => args = e;

        stack.Execute(new MergeableTestCommand(nodeId, 2));

        Assert.NotNull(args);
        Assert.Equal(CommandExecutionType.Merged, args.ExecutionType);
    }

    // =========================================================================
    // MERGE WINDOW CONFIGURATION
    // =========================================================================

    [Fact]
    public void SetMergeWindow_ThrowsOnNegative()
    {
        var stack = new CommandStack();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            stack.SetMergeWindow(TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void BreakMerge_PreventsNextMerge()
    {
        var stack = new CommandStack();
        var nodeId = Guid.NewGuid();
        stack.Execute(new MergeableTestCommand(nodeId, 1));

        stack.BreakMerge();
        stack.Execute(new MergeableTestCommand(nodeId, 2));

        Assert.Equal(2, stack.UndoCount);
    }

    // =========================================================================
    // TEST HELPERS
    // =========================================================================

    /// <summary>
    /// Simple test command for basic operations.
    /// </summary>
    private class TestCommand : IEditorCommand
    {
        public string Description { get; }
        public bool WasExecuted { get; private set; }
        public bool WasUndone { get; private set; }

        public TestCommand(string description)
        {
            Description = description;
        }

        public void Execute() => WasExecuted = true;
        public void Undo() => WasUndone = true;
        public void Reset()
        {
            WasExecuted = false;
            WasUndone = false;
        }
        public bool CanMergeWith(IEditorCommand other) => false;
        public bool TryMerge(IEditorCommand other) => false;
    }

    /// <summary>
    /// Test command that supports merging.
    /// </summary>
    private class MergeableTestCommand : IEditorCommand
    {
        public Guid NodeId { get; }
        public int Value { get; private set; }
        public string Description => $"Set value to {Value}";

        public MergeableTestCommand(Guid nodeId, int value)
        {
            NodeId = nodeId;
            Value = value;
        }

        public void Execute() { }
        public void Undo() { }

        public bool CanMergeWith(IEditorCommand other) =>
            other is MergeableTestCommand m && m.NodeId == NodeId;

        public bool TryMerge(IEditorCommand other)
        {
            if (!CanMergeWith(other)) return false;
            Value = ((MergeableTestCommand)other).Value;
            return true;
        }
    }
}
