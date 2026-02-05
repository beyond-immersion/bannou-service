// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Archives;

/// <summary>
/// Types of narrative kernels that can seed new storylines.
/// </summary>
public enum KernelType
{
    /// <summary>
    /// Character death - highest significance kernel.
    /// </summary>
    Death,

    /// <summary>
    /// Participation in major world events.
    /// </summary>
    HistoricalEvent,

    /// <summary>
    /// Past traumatic experiences.
    /// </summary>
    Trauma,

    /// <summary>
    /// Unachieved goals or unfinished business.
    /// </summary>
    UnfinishedBusiness,

    /// <summary>
    /// Deep negative encounters (grudge potential).
    /// </summary>
    Conflict,

    /// <summary>
    /// Strong positive relationships (legacy potential).
    /// </summary>
    DeepBond
}
