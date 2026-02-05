// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Arcs;

/// <summary>
/// The 6 emotional arc types from Reagan et al. (2016) computational analysis.
/// These represent the fundamental shapes of emotional trajectories in stories.
/// </summary>
public enum ArcType
{
    /// <summary>
    /// Rags to Riches - Monotonic rise from negative to positive.
    /// Pattern: Steady emotional ascent throughout the narrative.
    /// Examples: Rocky, The Pursuit of Happyness.
    /// </summary>
    RagsToRiches = 1,

    /// <summary>
    /// Tragedy (Riches to Rags) - Monotonic fall from positive to negative.
    /// Pattern: Steady emotional decline throughout the narrative.
    /// Examples: Hamlet, Flowers for Algernon.
    /// </summary>
    Tragedy = 2,

    /// <summary>
    /// Man in a Hole - Fall-rise (U shape).
    /// Pattern: Initial stability, fall into trouble, recovery.
    /// The most common arc in popular fiction.
    /// Examples: A Christmas Carol, Finding Nemo.
    /// </summary>
    ManInHole = 3,

    /// <summary>
    /// Icarus - Rise-fall (inverted U shape).
    /// Pattern: Rise to success, then collapse.
    /// Examples: The Great Gatsby, Breaking Bad.
    /// </summary>
    Icarus = 4,

    /// <summary>
    /// Cinderella - Rise-fall-rise (W inverted shape).
    /// Pattern: Rise, setback, ultimate triumph.
    /// Most satisfying for audiences according to download data.
    /// Examples: Pride and Prejudice, Rocky.
    /// </summary>
    Cinderella = 5,

    /// <summary>
    /// Oedipus - Fall-rise-fall (M shape).
    /// Pattern: Fall, false hope, final catastrophe.
    /// Examples: Oedipus Rex, 1984.
    /// </summary>
    Oedipus = 6
}
