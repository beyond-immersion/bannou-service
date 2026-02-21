// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Arcs;
using BeyondImmersion.Bannou.StorylineTheory.Planning;
using BeyondImmersion.Bannou.StorylineTheory.Spectrums;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Planning;

/// <summary>
/// Complete storyline plan output.
/// </summary>
public sealed class StorylinePlan
{
    /// <summary>
    /// Unique plan identifier.
    /// </summary>
    public required Guid PlanId { get; init; }

    /// <summary>
    /// The arc type used for this plan.
    /// </summary>
    public required ArcType ArcType { get; init; }

    /// <summary>
    /// The genre this plan is for.
    /// </summary>
    public required string Genre { get; init; }

    /// <summary>
    /// Optional subgenre.
    /// </summary>
    public required string? Subgenre { get; init; }

    /// <summary>
    /// The primary spectrum for emotional progression.
    /// </summary>
    public required SpectrumType PrimarySpectrum { get; init; }

    /// <summary>
    /// The planned phases.
    /// </summary>
    public required StorylinePlanPhase[] Phases { get; init; }

    /// <summary>
    /// Initial world state at plan start.
    /// </summary>
    public required WorldState InitialState { get; init; }

    /// <summary>
    /// Projected world state at plan completion.
    /// </summary>
    public required WorldState ProjectedEndState { get; init; }

    /// <summary>
    /// Core events that MUST occur (obligatory scenes).
    /// </summary>
    public required string[] RequiredCoreEvents { get; init; }
}
