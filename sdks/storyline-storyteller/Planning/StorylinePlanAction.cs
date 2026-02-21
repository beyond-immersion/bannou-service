// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineStoryteller.Actions;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Planning;

/// <summary>
/// A planned action in a storyline.
/// </summary>
public sealed class StorylinePlanAction
{
    /// <summary>
    /// The action ID.
    /// </summary>
    public required string ActionId { get; init; }

    /// <summary>
    /// Sequence index within the plan.
    /// </summary>
    public required int SequenceIndex { get; init; }

    /// <summary>
    /// Effects this action will apply.
    /// </summary>
    public required ActionEffect[] Effects { get; init; }

    /// <summary>
    /// Narrative effect on the emotional arc.
    /// </summary>
    public required NarrativeEffect NarrativeEffect { get; init; }

    /// <summary>
    /// Whether this is a core event (obligatory scene).
    /// </summary>
    public required bool IsCoreEvent { get; init; }

    /// <summary>
    /// If this action is part of a chain, the ID of the action it was chained from.
    /// </summary>
    public string? ChainedFrom { get; init; }
}
