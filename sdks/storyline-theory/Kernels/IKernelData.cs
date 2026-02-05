// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Kernels;

/// <summary>
/// Marker interface for type-specific kernel data.
/// Each kernel type has associated data that provides context for story composition.
/// </summary>
public interface IKernelData
{
}

/// <summary>
/// Data for Death kernels - character death information.
/// </summary>
public sealed class DeathKernelData : IKernelData
{
    /// <summary>
    /// The cause of death if known (e.g., "battle wounds", "poison", "old age").
    /// </summary>
    public required string? CauseOfDeath { get; init; }

    /// <summary>
    /// Location where death occurred if known.
    /// </summary>
    public required Guid? LocationId { get; init; }

    /// <summary>
    /// When the death occurred.
    /// </summary>
    public required DateTimeOffset DeathDate { get; init; }

    /// <summary>
    /// Character who caused the death, if applicable.
    /// Enables revenge arcs when populated.
    /// </summary>
    public Guid? KillerId { get; init; }
}

/// <summary>
/// Data for Trauma kernels - past traumatic experiences.
/// </summary>
public sealed class TraumaKernelData : IKernelData
{
    /// <summary>
    /// Category of trauma (e.g., "betrayal", "loss", "violence", "abandonment").
    /// </summary>
    public required string TraumaType { get; init; }

    /// <summary>
    /// Description of the traumatic event.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Character who caused the trauma, if applicable.
    /// Enables confrontation or forgiveness arcs.
    /// </summary>
    public Guid? PerpetratorId { get; init; }
}

/// <summary>
/// Data for Conflict kernels - high-impact negative encounters.
/// </summary>
public sealed class ConflictKernelData : IKernelData
{
    /// <summary>
    /// The encounter that created this conflict.
    /// </summary>
    public required Guid EncounterId { get; init; }

    /// <summary>
    /// Other characters involved in the conflict.
    /// </summary>
    public required Guid[] OtherCharacterIds { get; init; }

    /// <summary>
    /// How negative the sentiment was (-1.0 to 0).
    /// Lower values indicate more severe conflicts.
    /// </summary>
    public required double SentimentImpact { get; init; }

    /// <summary>
    /// Type of conflict if categorized (e.g., "betrayal", "combat", "theft").
    /// </summary>
    public required string? ConflictType { get; init; }
}

/// <summary>
/// Data for DeepBond kernels - strong positive relationships.
/// </summary>
public sealed class DeepBondKernelData : IKernelData
{
    /// <summary>
    /// The character with whom the bond exists.
    /// </summary>
    public required Guid BondedCharacterId { get; init; }

    /// <summary>
    /// Average sentiment across encounters (0 to 1.0).
    /// Higher values indicate stronger positive bonds.
    /// </summary>
    public required double AverageSentiment { get; init; }

    /// <summary>
    /// Number of encounters with this character.
    /// More encounters indicate deeper relationships.
    /// </summary>
    public required int EncounterCount { get; init; }

    /// <summary>
    /// Type of relationship if known (e.g., "mentor", "friend", "family", "lover").
    /// </summary>
    public required string? RelationshipType { get; init; }
}

/// <summary>
/// Data for UnfinishedBusiness kernels - incomplete goals.
/// </summary>
public sealed class UnfinishedBusinessKernelData : IKernelData
{
    /// <summary>
    /// All unfinished goals for the character.
    /// </summary>
    public required string[] Goals { get; init; }

    /// <summary>
    /// The primary unfinished goal, if one can be identified.
    /// </summary>
    public required string? PrimaryGoal { get; init; }
}

/// <summary>
/// Data for HistoricalEvent kernels - major event participation.
/// </summary>
public sealed class HistoricalEventKernelData : IKernelData
{
    /// <summary>
    /// Code identifying the historical event.
    /// </summary>
    public required string EventCode { get; init; }

    /// <summary>
    /// Character's role in the event (e.g., "participant", "witness", "instigator", "victim").
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// Reference to the event record if available.
    /// </summary>
    public required Guid? EventId { get; init; }
}

/// <summary>
/// Data for SignificantAsset kernels - important possessions.
/// </summary>
public sealed class SignificantAssetKernelData : IKernelData
{
    /// <summary>
    /// The item or asset identifier.
    /// </summary>
    public required Guid AssetId { get; init; }

    /// <summary>
    /// Type of asset (e.g., "weapon", "artifact", "document", "inheritance").
    /// </summary>
    public required string AssetType { get; init; }

    /// <summary>
    /// Description of why this asset is significant.
    /// </summary>
    public required string Significance { get; init; }

    /// <summary>
    /// Whether the character still possesses this asset.
    /// Lost assets create recovery/quest arcs.
    /// </summary>
    public required bool StillPossessed { get; init; }
}

/// <summary>
/// Data for BrokenPromise kernels - unfulfilled commitments.
/// </summary>
public sealed class BrokenPromiseKernelData : IKernelData
{
    /// <summary>
    /// Description of the promise or commitment.
    /// </summary>
    public required string PromiseDescription { get; init; }

    /// <summary>
    /// Who the promise was made to.
    /// </summary>
    public required Guid? PromiseeId { get; init; }

    /// <summary>
    /// Whether this character broke the promise (true) or was the victim of a broken promise (false).
    /// </summary>
    public required bool CharacterBrokePromise { get; init; }

    /// <summary>
    /// Reference to the contract if this was a formal agreement.
    /// </summary>
    public Guid? ContractId { get; init; }
}
