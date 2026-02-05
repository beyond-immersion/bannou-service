// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineStoryteller.Actions;

/// <summary>
/// A chain of actions that must be executed together.
/// </summary>
public sealed class ActionChain
{
    /// <summary>
    /// The sequence of actions in this chain.
    /// </summary>
    public required StoryAction[] Actions { get; init; }

    /// <summary>
    /// The total cost of all actions in the chain.
    /// </summary>
    public required double TotalCost { get; init; }

    /// <summary>
    /// Builds an action chain starting from the given action.
    /// </summary>
    public static ActionChain Build(StoryAction startAction)
    {
        var chain = new List<StoryAction> { startAction };
        var current = startAction;

        while (current.ChainedAction != null)
        {
            current = ActionRegistry.Get(current.ChainedAction);
            chain.Add(current);
        }

        return new ActionChain
        {
            Actions = chain.ToArray(),
            TotalCost = chain.Sum(a => a.Cost)
        };
    }
}
