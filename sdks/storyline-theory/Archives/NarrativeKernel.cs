// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Archives;

/// <summary>
/// A narrative kernel - an essential event or condition that could seed a new storyline.
/// Kernels are extracted from archives and represent story opportunities.
/// </summary>
/// <remarks>
/// The concept of "kernels" comes from narratology - events that are essential to the story
/// structure, as opposed to "satellites" which are optional embellishments. In our usage,
/// kernels are potential story seeds: events, relationships, or conditions that have
/// enough narrative weight to initiate new storylines.
/// </remarks>
public sealed class NarrativeKernel
{
    /// <summary>
    /// The type of kernel (death, trauma, conflict, etc.).
    /// </summary>
    public required KernelType Type { get; init; }

    /// <summary>
    /// The significance score (0.0 to 1.0).
    /// Higher significance means stronger story potential.
    /// </summary>
    public required double Significance { get; init; }

    /// <summary>
    /// A human-readable description of the kernel.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Additional data specific to the kernel type.
    /// Keys and values depend on the kernel type.
    /// </summary>
    public required IReadOnlyDictionary<string, object> Data { get; init; }

    /// <summary>
    /// Optional: The ID of the character this kernel relates to.
    /// </summary>
    public Guid? CharacterId { get; init; }

    /// <summary>
    /// Optional: The ID of the realm this kernel relates to.
    /// </summary>
    public Guid? RealmId { get; init; }

    /// <summary>
    /// Optional: IDs of other characters involved in this kernel.
    /// </summary>
    public Guid[]? RelatedCharacterIds { get; init; }

    /// <summary>
    /// Gets a data value by key, or null if not present.
    /// </summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <param name="key">The data key.</param>
    /// <returns>The value, or default if not found or wrong type.</returns>
    public T? GetData<T>(string key)
    {
        if (Data.TryGetValue(key, out var value) && value is T typed)
        {
            return typed;
        }
        return default;
    }

    /// <summary>
    /// Gets suggested arc types for this kernel.
    /// </summary>
    /// <returns>Arc types that commonly pair with this kernel type.</returns>
    public IEnumerable<Arcs.ArcType> GetSuggestedArcs()
    {
        return Type switch
        {
            KernelType.Death => new[] { Arcs.ArcType.Tragedy, Arcs.ArcType.ManInHole, Arcs.ArcType.Oedipus },
            KernelType.Trauma => new[] { Arcs.ArcType.ManInHole, Arcs.ArcType.Cinderella },
            KernelType.Conflict => new[] { Arcs.ArcType.Icarus, Arcs.ArcType.Tragedy, Arcs.ArcType.ManInHole },
            KernelType.DeepBond => new[] { Arcs.ArcType.RagsToRiches, Arcs.ArcType.Cinderella },
            KernelType.Achievement => new[] { Arcs.ArcType.Icarus, Arcs.ArcType.RagsToRiches },
            KernelType.Secret => new[] { Arcs.ArcType.Oedipus, Arcs.ArcType.ManInHole },
            KernelType.BrokenPromise => new[] { Arcs.ArcType.Tragedy, Arcs.ArcType.Oedipus },
            KernelType.Transformation => new[] { Arcs.ArcType.RagsToRiches, Arcs.ArcType.Cinderella },
            KernelType.HistoricalEvent => new[] { Arcs.ArcType.ManInHole, Arcs.ArcType.Tragedy, Arcs.ArcType.Cinderella },
            KernelType.UnfinishedBusiness => new[] { Arcs.ArcType.ManInHole, Arcs.ArcType.RagsToRiches },
            _ => Array.Empty<Arcs.ArcType>()
        };
    }
}
