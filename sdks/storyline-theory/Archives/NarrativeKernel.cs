// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Archives;

/// <summary>
/// A narrative kernel - an essential event that could seed a new storyline.
/// </summary>
public sealed class NarrativeKernel
{
    /// <summary>
    /// The type of kernel.
    /// </summary>
    public required KernelType Type { get; init; }

    /// <summary>
    /// The significance score (0.0 to 1.0).
    /// </summary>
    public required double Significance { get; init; }

    /// <summary>
    /// A human-readable description of the kernel.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Additional data specific to the kernel type.
    /// </summary>
    public required Dictionary<string, object> Data { get; init; }
}
