// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Actants;
using BeyondImmersion.Bannou.StorylineTheory.Planning;
using BeyondImmersion.Bannou.StorylineTheory.Spectrums;
using BeyondImmersion.Bannou.StorylineStoryteller.Planning;
using BeyondImmersion.Bannou.StorylineStoryteller.Templates;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Composition;

/// <summary>
/// Context for story generation.
/// </summary>
public sealed class StoryContext
{
    /// <summary>
    /// The story template being followed.
    /// </summary>
    public required StoryTemplate Template { get; init; }

    /// <summary>
    /// The genre for this story.
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
    /// Participant character IDs (opaque to SDK).
    /// </summary>
    public required Guid[] CharacterIds { get; init; }

    /// <summary>
    /// Realm ID (opaque to SDK).
    /// </summary>
    public required Guid RealmId { get; init; }

    /// <summary>
    /// Greimas actant role assignments.
    /// Maps actant roles to character IDs.
    /// </summary>
    public required Dictionary<ActantRole, Guid[]> ActantAssignments { get; init; }

    /// <summary>
    /// Target action count. If null, derived from template default + genre modifier.
    /// Clamped to template's ActionCountRange.
    /// </summary>
    public int? TargetActionCount { get; init; }

    /// <summary>
    /// Current world state (updated during generation).
    /// </summary>
    public WorldState? CurrentState { get; set; }

    /// <summary>
    /// Progress tracking for position estimation.
    /// </summary>
    public StoryProgress Progress { get; } = new();
}
