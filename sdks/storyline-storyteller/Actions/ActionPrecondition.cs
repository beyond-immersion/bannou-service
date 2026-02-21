// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineStoryteller.Actions;

/// <summary>
/// Precondition for GOAP planning.
/// </summary>
public sealed class ActionPrecondition
{
    /// <summary>
    /// The world state key to check (e.g., "antagonist.known").
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The expected value (true, false, or numeric).
    /// </summary>
    public required object Value { get; init; }

    /// <summary>
    /// The comparison operator. Defaults to Equals.
    /// </summary>
    public ActionPreconditionOperator Operator { get; init; } = ActionPreconditionOperator.Equals;
}
