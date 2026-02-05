// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Archives;

/// <summary>
/// Types of narrative kernels that can seed new storylines.
/// Kernels are essential narrative events that could initiate or drive stories.
/// </summary>
public enum KernelType
{
    /// <summary>
    /// Character death - highest significance kernel.
    /// Death is always a major narrative event that can seed revenge, legacy, or mourning storylines.
    /// </summary>
    Death = 1,

    /// <summary>
    /// Participation in major world events (wars, disasters, political upheavals).
    /// Historical significance creates narrative hooks for characters connected to these events.
    /// </summary>
    HistoricalEvent = 2,

    /// <summary>
    /// Past traumatic experiences.
    /// Trauma creates psychological depth and can drive healing, revenge, or redemption arcs.
    /// </summary>
    Trauma = 3,

    /// <summary>
    /// Unachieved goals or unfinished business.
    /// Creates sequel hooks and continuation opportunities.
    /// </summary>
    UnfinishedBusiness = 4,

    /// <summary>
    /// Deep negative encounters (grudge potential).
    /// High-intensity negative interactions that could drive revenge or rivalry storylines.
    /// </summary>
    Conflict = 5,

    /// <summary>
    /// Strong positive relationships (legacy potential).
    /// Deep bonds that can drive protection, loyalty, or sacrifice storylines.
    /// </summary>
    DeepBond = 6,

    /// <summary>
    /// Significant achievement or accomplishment.
    /// Can seed stories of maintaining status, defending legacy, or hubris.
    /// </summary>
    Achievement = 7,

    /// <summary>
    /// Major secret or hidden truth.
    /// Revelation of secrets drives mystery and drama storylines.
    /// </summary>
    Secret = 8,

    /// <summary>
    /// Unfulfilled obligation or broken promise.
    /// Creates narrative tension and redemption opportunities.
    /// </summary>
    BrokenPromise = 9,

    /// <summary>
    /// Transformation or major character change.
    /// Points where a character fundamentally changed can anchor flashback or consequence stories.
    /// </summary>
    Transformation = 10
}
