// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Kernels;

/// <summary>
/// Categories of narrative kernels that can seed stories.
/// Based on Barthes/Chatman kernel theory applied to game archives.
/// </summary>
/// <remarks>
/// Kernels (Cardinal Functions) are branching points where choices occur;
/// they cannot be deleted without altering story logic. Satellites (Catalyzers)
/// are elaboration and texture that can be freely changed or removed.
/// When compressing or adapting stories, kernels MUST be preserved.
/// </remarks>
public enum KernelType
{
    /// <summary>
    /// Character death - always maximum significance for archived characters.
    /// Death is the ultimate irreversible state change.
    /// </summary>
    Death = 1,

    /// <summary>
    /// High-significance historical event participation.
    /// Wars, disasters, political upheavals that shaped the character.
    /// </summary>
    HistoricalEvent = 2,

    /// <summary>
    /// Traumatic backstory element.
    /// Betrayals, losses, violence that define character motivations.
    /// </summary>
    Trauma = 3,

    /// <summary>
    /// Unfinished goals creating narrative hooks.
    /// Unfulfilled promises, incomplete quests, unrealized ambitions.
    /// </summary>
    UnfinishedBusiness = 4,

    /// <summary>
    /// High-impact negative encounter (grudge potential).
    /// Deep conflicts that create lasting enmity.
    /// </summary>
    Conflict = 5,

    /// <summary>
    /// Deep positive relationship (legacy/protection potential).
    /// Strong bonds that motivate sacrifice or revenge.
    /// </summary>
    DeepBond = 6,

    /// <summary>
    /// Major possession or artifact with story potential.
    /// Items of significance that drive plots.
    /// </summary>
    SignificantAsset = 7,

    /// <summary>
    /// Unfulfilled oath, promise, or contract.
    /// Broken or incomplete commitments demanding resolution.
    /// </summary>
    BrokenPromise = 8
}
