// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Actants;
using BeyondImmersion.Bannou.StorylineTheory.Arcs;

namespace BeyondImmersion.Bannou.StorylineTheory.Kernels;

/// <summary>
/// An essential narrative event that can seed a storyline.
/// Kernels cannot be removed without breaking narrative causality.
/// </summary>
/// <remarks>
/// <para>
/// From narratology (Barthes/Chatman): Kernels are "cardinal functions" -
/// branching points where choices occur and the story's direction changes.
/// They contrast with "satellites" (catalyzers) which provide texture but
/// can be modified without altering the fundamental story.
/// </para>
/// <para>
/// The KernelExtractor identifies kernels from archived character data.
/// The lib-storyline plugin then uses these kernels to select appropriate
/// story templates and assign actant roles for story composition.
/// </para>
/// </remarks>
public sealed class NarrativeKernel
{
    /// <summary>
    /// Unique identifier for this kernel instance.
    /// </summary>
    public required Guid KernelId { get; init; }

    /// <summary>
    /// Category of kernel determining extraction logic and arc compatibility.
    /// </summary>
    public required KernelType Type { get; init; }

    /// <summary>
    /// Significance score from 0.0 to 1.0.
    /// Higher values indicate greater story potential.
    /// Death kernels are always 1.0 for archived characters.
    /// </summary>
    public required double Significance { get; init; }

    /// <summary>
    /// The resource ID from which this kernel was extracted.
    /// </summary>
    public required Guid SourceResourceId { get; init; }

    /// <summary>
    /// The type of resource from which this kernel was extracted
    /// (e.g., "character", "realm").
    /// </summary>
    public required string SourceResourceType { get; init; }

    /// <summary>
    /// All characters involved in this kernel event.
    /// Used for actant assignment during story composition.
    /// </summary>
    public required Guid[] InvolvedCharacterIds { get; init; }

    /// <summary>
    /// Suggested actant roles for involved characters.
    /// Key = CharacterId, Value = suggested ActantRole.
    /// Null if no suggestions are available.
    /// </summary>
    /// <remarks>
    /// These are suggestions based on the kernel context. For example,
    /// a Conflict kernel might suggest the source character as Subject
    /// and the other party as Opponent. The story composer may override
    /// these based on the chosen template and narrative goals.
    /// </remarks>
    public Dictionary<Guid, ActantRole>? SuggestedActants { get; init; }

    /// <summary>
    /// Arc templates compatible with this kernel.
    /// Based on kernel type → arc affinity mapping from narratology.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Affinity mappings are based on:
    /// - Death → Tragedy, ManInHole (revenge), Oedipus (tragic cycle)
    /// - Trauma → ManInHole (recovery), Cinderella (overcoming)
    /// - Conflict → ManInHole, Tragedy
    /// - DeepBond → RagsToRiches, Cinderella
    /// </para>
    /// </remarks>
    public required ArcType[] CompatibleArcs { get; init; }

    /// <summary>
    /// Genre affinities for this kernel.
    /// Higher values (0.0 to 1.0) indicate better fit for that genre.
    /// Genre keys follow Story Grid conventions (e.g., "action", "love", "horror").
    /// </summary>
    public required Dictionary<string, double> GenreAffinities { get; init; }

    /// <summary>
    /// Type-specific kernel data containing extraction details.
    /// Cast to the appropriate IKernelData implementation based on Type.
    /// </summary>
    public required IKernelData Data { get; init; }
}
