// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Spectrums;

/// <summary>
/// A complete spectrum definition loaded from narrative-state.yaml.
/// Defines a Life Value spectrum with its four stages and associated metadata.
/// </summary>
public sealed class SpectrumDefinition
{
    /// <summary>
    /// The spectrum type identifier.
    /// </summary>
    public required SpectrumType Type { get; init; }

    /// <summary>
    /// The spectrum code (e.g., "LIFE_DEATH").
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// The human-readable name (e.g., "Life vs Death").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The Maslow domain this spectrum belongs to (e.g., "survival", "safety", "connection").
    /// </summary>
    public required string Domain { get; init; }

    /// <summary>
    /// The Maslow hierarchy level (1-5).
    /// </summary>
    public required int MaslowLevel { get; init; }

    /// <summary>
    /// The positive pole label (e.g., "Life").
    /// </summary>
    public required string PositiveLabel { get; init; }

    /// <summary>
    /// The negative pole label (e.g., "Death").
    /// </summary>
    public required string NegativeLabel { get; init; }

    /// <summary>
    /// The four stages of the spectrum: Positive, Contrary, Negative, and Negation of Negation.
    /// </summary>
    public required SpectrumPole[] Stages { get; init; }

    /// <summary>
    /// The core human need this spectrum addresses.
    /// </summary>
    public required string CoreNeed { get; init; }

    /// <summary>
    /// The primary emotion evoked by stories on this spectrum.
    /// </summary>
    public required string CoreEmotion { get; init; }

    /// <summary>
    /// Genres that use this as their primary spectrum.
    /// </summary>
    public required string[] PrimaryGenres { get; init; }

    /// <summary>
    /// The dramatic question this spectrum poses.
    /// </summary>
    public required string DramaticQuestion { get; init; }

    /// <summary>
    /// Gets the pole at a specific stage by label.
    /// </summary>
    /// <param name="label">The pole label (e.g., "positive", "contrary", "negative", "negation_of_negation").</param>
    /// <returns>The pole, or null if not found.</returns>
    public SpectrumPole? GetPole(string label)
    {
        return Stages.FirstOrDefault(s =>
            s.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Interpolates a display label for a given numeric value on this spectrum.
    /// </summary>
    /// <param name="value">The numeric value (-1.0 to 1.0).</param>
    /// <returns>The closest pole label for the value.</returns>
    public string GetLabelForValue(double value)
    {
        var closest = Stages.OrderBy(s => Math.Abs(s.Value - value)).First();
        return closest.Label;
    }
}
