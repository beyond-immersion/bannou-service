// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Spectrums;

namespace BeyondImmersion.Bannou.StorylineTheory.Planning;

/// <summary>
/// Combined state model for GOAP planning, containing narrative state,
/// story facts, and position in the story.
/// </summary>
public sealed class WorldState
{
    /// <summary>
    /// The current emotional/narrative state on the spectrum.
    /// </summary>
    public required NarrativeState NarrativeState { get; init; }

    /// <summary>
    /// Boolean/numeric facts about the story world.
    /// </summary>
    public required IReadOnlyDictionary<string, object> Facts { get; init; }

    /// <summary>
    /// Position in the story (0.0 = start, 1.0 = end).
    /// </summary>
    public required double Position { get; init; }

    /// <summary>
    /// Returns a new WorldState with the specified fact added or updated.
    /// </summary>
    public WorldState WithFact(string key, object value)
    {
        var newFacts = new Dictionary<string, object>(Facts)
        {
            [key] = value
        };
        return new WorldState
        {
            NarrativeState = NarrativeState,
            Facts = newFacts,
            Position = Position
        };
    }

    /// <summary>
    /// Returns a new WorldState with the specified narrative state.
    /// </summary>
    public WorldState WithNarrativeState(NarrativeState state)
    {
        return new WorldState
        {
            NarrativeState = state,
            Facts = Facts,
            Position = Position
        };
    }

    /// <summary>
    /// Returns a new WorldState with the specified position.
    /// </summary>
    public WorldState WithPosition(double position)
    {
        return new WorldState
        {
            NarrativeState = NarrativeState,
            Facts = Facts,
            Position = position
        };
    }
}
