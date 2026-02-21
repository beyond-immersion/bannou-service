// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineStoryteller.Planning;

/// <summary>
/// Urgency levels for GOAP planning that affect search parameters.
/// </summary>
public enum PlanningUrgency
{
    /// <summary>
    /// Low urgency: max_iterations: 1000, beam_width: 20.
    /// </summary>
    Low,

    /// <summary>
    /// Medium urgency: max_iterations: 500, beam_width: 15.
    /// </summary>
    Medium,

    /// <summary>
    /// High urgency: max_iterations: 200, beam_width: 10.
    /// </summary>
    High
}
